using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using System.Xml.Linq;

namespace LizziesMod
{
    public sealed class ModPortalFile
    {
        public string RelativePath;
        public string Url;
        public string Sha256;
        public long Size;
    }

    public sealed class ModPortalPackage
    {
        public string Id;
        public string DisplayName;
        public string Version;
        public string Description;
        public string Author;
        public string Website;
        public string GameVersion;
        public List<ModPortalFile> Files = new List<ModPortalFile>();
    }

    public static class ModPortalManager
    {
        public const string SettingsFileName = "ModPortalSettings.xml";
        public const string CatalogCacheFileName = "ModPortalCatalog.cache.xml";
        public const string PortalManifestFileName = "LizziesModPortal.xml";

        private const string CatalogRootName = "ModPortalCatalog";
        private const string PackageElementName = "Package";
        private const string FileElementName = "File";
        private static readonly object Sync = new object();
        private static readonly Regex PackageIdPattern = new Regex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,79}$", RegexOptions.Compiled);
        private static readonly Regex ChecksumPattern = new Regex("^[a-fA-F0-9]{64}$", RegexOptions.Compiled);
        private static readonly List<ModPortalPackage> CatalogPackages = new List<ModPortalPackage>();

        private static string coreModPath = "";
        private static string catalogEndpoint = "";
        private static string status = "Mod Portal has not been initialized.";
        private static bool isRefreshing;
        private static bool isInstalling;

        public static string Status
        {
            get
            {
                lock (Sync)
                {
                    return status;
                }
            }
        }

        public static bool IsWorking
        {
            get
            {
                lock (Sync)
                {
                    return isRefreshing || isInstalling;
                }
            }
        }

        public static bool HasConfiguredEndpoint
        {
            get
            {
                lock (Sync)
                {
                    return !string.IsNullOrEmpty(catalogEndpoint);
                }
            }
        }

        public static void Initialize(Mod coreMod)
        {
            lock (Sync)
            {
                coreModPath = coreMod != null ? coreMod.Path ?? "" : "";
                catalogEndpoint = LoadCatalogEndpoint();
                CatalogPackages.Clear();
                LoadCachedCatalog();

                if (CatalogPackages.Count > 0)
                {
                    status = "Loaded " + CatalogPackages.Count + " cached portal package(s).";
                }
                else if (string.IsNullOrEmpty(catalogEndpoint))
                {
                    status = "No portal endpoint is configured.";
                }
                else
                {
                    status = "Portal catalog is ready to refresh.";
                }
            }
        }

        public static List<ModPortalPackage> GetPackages()
        {
            lock (Sync)
            {
                return CatalogPackages
                    .OrderBy(package => package.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Select(ClonePackage)
                    .ToList();
            }
        }

        public static ModPortalPackage FindPackage(string modName, string requiredVersion)
        {
            lock (Sync)
            {
                ModPortalPackage package = CatalogPackages.FirstOrDefault(candidate =>
                    candidate.Id.Equals(modName ?? "", StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(requiredVersion) || candidate.Version.Equals(requiredVersion, StringComparison.OrdinalIgnoreCase)));
                return package == null ? null : ClonePackage(package);
            }
        }

        public static bool BeginRefresh(out string message)
        {
            lock (Sync)
            {
                if (isRefreshing || isInstalling)
                {
                    message = "The Mod Portal is already working.";
                    return false;
                }

                if (string.IsNullOrEmpty(catalogEndpoint))
                {
                    status = "No portal endpoint is configured. Add a valid HTTPS endpoint to " + SettingsFileName + ".";
                    message = status;
                    return false;
                }

                isRefreshing = true;
                status = "Refreshing portal catalog...";
                string endpoint = catalogEndpoint;
                ThreadPool.QueueUserWorkItem(state => RefreshCatalogWorker((string)state), endpoint);
                message = status;
                return true;
            }
        }

        public static bool BeginInstall(string packageId, out string message)
        {
            ModPortalPackage package;
            lock (Sync)
            {
                if (isRefreshing || isInstalling)
                {
                    message = "The Mod Portal is already working.";
                    return false;
                }

                package = CatalogPackages.FirstOrDefault(candidate => candidate.Id.Equals(packageId ?? "", StringComparison.OrdinalIgnoreCase));
                if (package == null)
                {
                    message = "That portal package is no longer available.";
                    return false;
                }

                package = ClonePackage(package);
                isInstalling = true;
                status = "Preparing " + GetDisplayName(package) + "...";
                ThreadPool.QueueUserWorkItem(state => InstallPackageWorker((ModPortalPackage)state), package);
                message = status;
                return true;
            }
        }

        private static void RefreshCatalogWorker(string endpoint)
        {
            try
            {
                string catalogXml;
                using (WebClient client = CreateWebClient())
                {
                    catalogXml = client.DownloadString(endpoint);
                }

                List<ModPortalPackage> parsedPackages = ParseCatalog(catalogXml);
                SaveCatalogCache(catalogXml);

                lock (Sync)
                {
                    CatalogPackages.Clear();
                    CatalogPackages.AddRange(parsedPackages);
                    status = "Loaded " + CatalogPackages.Count + " portal package(s).";
                }
            }
            catch (Exception exception)
            {
                Logger.Error("[ModPortal] Could not refresh catalog: " + exception.Message);
                lock (Sync)
                {
                    status = "Could not refresh portal catalog: " + exception.Message;
                }
            }
            finally
            {
                lock (Sync)
                {
                    isRefreshing = false;
                }
            }
        }

        private static void InstallPackageWorker(ModPortalPackage package)
        {
            string stagingPath = "";
            string targetPath = "";
            string backupPath = "";
            bool existingPackageMoved = false;

            try
            {
                ValidatePackage(package);
                string modsRoot = Path.GetDirectoryName(coreModPath);
                if (string.IsNullOrEmpty(modsRoot)) throw new InvalidOperationException("The installed Mods folder could not be resolved.");

                targetPath = GetSafePackagePath(modsRoot, package.Id);
                if (PathsEqual(targetPath, coreModPath)) throw new InvalidOperationException("The core LizziesMod package cannot be installed through the portal.");

                if (Directory.Exists(targetPath) && !File.Exists(Path.Combine(targetPath, PortalManifestFileName)))
                {
                    throw new InvalidOperationException("'" + package.Id + "' is already installed and is not portal-managed. It will not be overwritten.");
                }

                stagingPath = Path.Combine(modsRoot, ".lizziesmod-portal-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stagingPath);

                for (int index = 0; index < package.Files.Count; index++)
                {
                    ModPortalFile file = package.Files[index];
                    SetStatus("Downloading " + GetDisplayName(package) + " (" + (index + 1) + "/" + package.Files.Count + ")...");
                    byte[] content;
                    using (WebClient client = CreateWebClient())
                    {
                        content = client.DownloadData(file.Url);
                    }

                    if (file.Size > 0 && content.LongLength != file.Size)
                    {
                        throw new InvalidOperationException("Downloaded size did not match the catalog for '" + file.RelativePath + "'.");
                    }

                    string actualChecksum = ComputeSha256(content);
                    if (!actualChecksum.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Checksum verification failed for '" + file.RelativePath + "'.");
                    }

                    ValidateXml(content, file.RelativePath);
                    string destinationPath = GetSafeConfigPath(stagingPath, file.RelativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                    File.WriteAllBytes(destinationPath, content);
                }

                WriteModInfo(stagingPath, package);
                WritePortalManifest(stagingPath, package);

                if (Directory.Exists(targetPath))
                {
                    backupPath = targetPath + ".lizziesmod-backup-" + Guid.NewGuid().ToString("N");
                    Directory.Move(targetPath, backupPath);
                    existingPackageMoved = true;
                }

                Directory.Move(stagingPath, targetPath);
                stagingPath = "";

                if (!string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath))
                {
                    Directory.Delete(backupPath, true);
                }

                SetStatus("Installed " + GetDisplayName(package) + ". Restart the client to load it.");
                Logger.Info("[ModPortal] Installed XML-only package '" + package.Id + "' version '" + package.Version + "'.");
            }
            catch (Exception exception)
            {
                if (existingPackageMoved && !string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath) && !Directory.Exists(targetPath))
                {
                    try
                    {
                        Directory.Move(backupPath, targetPath);
                    }
                    catch (Exception restoreException)
                    {
                        Logger.Error("[ModPortal] Failed to restore the prior package after an install error: " + restoreException.Message);
                    }
                }

                Logger.Error("[ModPortal] Could not install '" + package.Id + "': " + exception.Message);
                SetStatus("Could not install " + GetDisplayName(package) + ": " + exception.Message);
            }
            finally
            {
                if (!string.IsNullOrEmpty(stagingPath) && Directory.Exists(stagingPath))
                {
                    try
                    {
                        Directory.Delete(stagingPath, true);
                    }
                    catch (Exception cleanupException)
                    {
                        Logger.Warning("[ModPortal] Could not remove staging folder: " + cleanupException.Message);
                    }
                }

                lock (Sync)
                {
                    isInstalling = false;
                }
            }
        }

        private static void LoadCachedCatalog()
        {
            string cachePath = GetCachePath();
            if (string.IsNullOrEmpty(cachePath) || !File.Exists(cachePath)) return;

            try
            {
                List<ModPortalPackage> cachedPackages = ParseCatalog(File.ReadAllText(cachePath));
                CatalogPackages.AddRange(cachedPackages);
            }
            catch (Exception exception)
            {
                Logger.Warning("[ModPortal] Ignored invalid cached catalog: " + exception.Message);
            }
        }

        private static string LoadCatalogEndpoint()
        {
            string settingsPath = GetSettingsPath();
            if (string.IsNullOrEmpty(settingsPath) || !File.Exists(settingsPath)) return "";

            try
            {
                XDocument document = LoadXml(File.ReadAllText(settingsPath));
                string endpoint = document.Root == null ? "" : ((string)document.Root.Attribute("endpoint") ?? "").Trim();
                if (string.IsNullOrEmpty(endpoint)) return "";

                Uri endpointUri;
                if (!Uri.TryCreate(endpoint, UriKind.Absolute, out endpointUri) || endpointUri.Scheme != Uri.UriSchemeHttps)
                {
                    Logger.Warning("[ModPortal] Ignored non-HTTPS endpoint in '" + SettingsFileName + "'.");
                    return "";
                }

                return endpointUri.AbsoluteUri;
            }
            catch (Exception exception)
            {
                Logger.Warning("[ModPortal] Could not read '" + SettingsFileName + "': " + exception.Message);
                return "";
            }
        }

        private static List<ModPortalPackage> ParseCatalog(string catalogXml)
        {
            XDocument document = LoadXml(catalogXml);
            if (document.Root == null || !document.Root.Name.LocalName.Equals(CatalogRootName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Catalog root must be '" + CatalogRootName + "'.");
            }

            List<ModPortalPackage> packages = new List<ModPortalPackage>();
            HashSet<string> packageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (XElement packageElement in document.Root.Elements(PackageElementName))
            {
                ModPortalPackage package = new ModPortalPackage
                {
                    Id = GetRequiredAttribute(packageElement, "id"),
                    DisplayName = GetOptionalAttribute(packageElement, "display_name"),
                    Version = GetRequiredAttribute(packageElement, "version"),
                    Description = GetOptionalAttribute(packageElement, "description"),
                    Author = GetOptionalAttribute(packageElement, "author"),
                    Website = GetOptionalAttribute(packageElement, "website"),
                    GameVersion = GetOptionalAttribute(packageElement, "game_version")
                };

                if (!PackageIdPattern.IsMatch(package.Id)) throw new InvalidOperationException("Package id '" + package.Id + "' is invalid.");
                if (!packageIds.Add(package.Id)) throw new InvalidOperationException("Catalog contains duplicate package id '" + package.Id + "'.");
                if (string.IsNullOrEmpty(package.DisplayName)) package.DisplayName = package.Id;

                foreach (XElement fileElement in packageElement.Elements(FileElementName))
                {
                    long size = 0;
                    string sizeText = GetOptionalAttribute(fileElement, "size");
                    if (!string.IsNullOrEmpty(sizeText) && (!long.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out size) || size < 1))
                    {
                        throw new InvalidOperationException("Package '" + package.Id + "' has an invalid size for one of its files.");
                    }

                    ModPortalFile file = new ModPortalFile
                    {
                        RelativePath = NormalizeRelativePath(GetRequiredAttribute(fileElement, "path")),
                        Url = GetRequiredAttribute(fileElement, "url"),
                        Sha256 = GetRequiredAttribute(fileElement, "sha256"),
                        Size = size
                    };
                    ValidatePortalFile(file, package.Id);
                    if (package.Files.Any(existing => existing.RelativePath.Equals(file.RelativePath, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new InvalidOperationException("Package '" + package.Id + "' lists '" + file.RelativePath + "' more than once.");
                    }

                    package.Files.Add(file);
                }

                if (package.Files.Count == 0) throw new InvalidOperationException("Package '" + package.Id + "' does not contain any XML files.");
                packages.Add(package);
            }

            return packages;
        }

        private static void ValidatePackage(ModPortalPackage package)
        {
            if (package == null) throw new InvalidOperationException("The portal package is missing.");
            if (!PackageIdPattern.IsMatch(package.Id)) throw new InvalidOperationException("The portal package id is invalid.");
            if (package.Files == null || package.Files.Count == 0) throw new InvalidOperationException("The portal package contains no XML files.");
            foreach (ModPortalFile file in package.Files)
            {
                ValidatePortalFile(file, package.Id);
            }
        }

        private static void ValidatePortalFile(ModPortalFile file, string packageId)
        {
            if (file == null || !IsSafeConfigXmlPath(file.RelativePath))
            {
                throw new InvalidOperationException("Package '" + packageId + "' contains a file outside Config/*.xml.");
            }

            Uri fileUri;
            if (!Uri.TryCreate(file.Url, UriKind.Absolute, out fileUri) || fileUri.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("Package '" + packageId + "' contains a non-HTTPS file URL.");
            }

            if (!ChecksumPattern.IsMatch(file.Sha256 ?? ""))
            {
                throw new InvalidOperationException("Package '" + packageId + "' contains an invalid SHA-256 checksum.");
            }
        }

        private static bool IsSafeConfigXmlPath(string path)
        {
            string normalizedPath = NormalizeRelativePath(path);
            return !string.IsNullOrEmpty(normalizedPath) &&
                   normalizedPath.StartsWith("Config/", StringComparison.OrdinalIgnoreCase) &&
                   normalizedPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                   normalizedPath.IndexOf("..", StringComparison.Ordinal) < 0 &&
                   normalizedPath.IndexOf(':') < 0 &&
                   normalizedPath.IndexOf("//", StringComparison.Ordinal) < 0;
        }

        private static string GetSafePackagePath(string modsRoot, string packageId)
        {
            string rootPath = Path.GetFullPath(modsRoot + Path.DirectorySeparatorChar);
            string packagePath = Path.GetFullPath(Path.Combine(modsRoot, packageId));
            if (!packagePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Package installation path was rejected.");
            }

            return packagePath;
        }

        private static string GetSafeConfigPath(string packageRoot, string relativePath)
        {
            string rootPath = Path.GetFullPath(packageRoot + Path.DirectorySeparatorChar);
            string destinationPath = Path.GetFullPath(Path.Combine(packageRoot, NormalizeRelativePath(relativePath).Replace('/', Path.DirectorySeparatorChar)));
            if (!destinationPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Portal file path was rejected.");
            }

            return destinationPath;
        }

        private static void ValidateXml(byte[] content, string fileName)
        {
            XmlReaderSettings settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit
            };
            using (MemoryStream stream = new MemoryStream(content, false))
            using (XmlReader reader = XmlReader.Create(stream, settings))
            {
                XDocument.Load(reader);
            }
        }

        private static void WriteModInfo(string packagePath, ModPortalPackage package)
        {
            XElement root = new XElement("xml",
                new XElement("Name", new XAttribute("value", package.Id)),
                new XElement("DisplayName", new XAttribute("value", GetDisplayName(package))),
                new XElement("Description", new XAttribute("value", package.Description ?? "")),
                new XElement("Author", new XAttribute("value", package.Author ?? "Mod Portal")),
                new XElement("Version", new XAttribute("value", package.Version ?? "")),
                new XElement("Website", new XAttribute("value", package.Website ?? "")),
                new XElement("SkipWithAntiCheat", new XAttribute("value", "true")));
            SaveXml(Path.Combine(packagePath, "ModInfo.xml"), new XDocument(new XDeclaration("1.0", "utf-8", null), root));
        }

        private static void WritePortalManifest(string packagePath, ModPortalPackage package)
        {
            XElement root = new XElement("LizziesModPortalPackage",
                new XAttribute("id", package.Id),
                new XAttribute("version", package.Version ?? ""),
                new XAttribute("installed_utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)));
            foreach (ModPortalFile file in package.Files)
            {
                root.Add(new XElement("File",
                    new XAttribute("path", file.RelativePath),
                    new XAttribute("sha256", file.Sha256),
                    new XAttribute("size", file.Size.ToString(CultureInfo.InvariantCulture))));
            }

            SaveXml(Path.Combine(packagePath, PortalManifestFileName), new XDocument(new XDeclaration("1.0", "utf-8", null), root));
        }

        private static void SaveCatalogCache(string catalogXml)
        {
            string cachePath = GetCachePath();
            if (string.IsNullOrEmpty(cachePath)) return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath));
                string temporaryPath = cachePath + ".tmp";
                File.WriteAllText(temporaryPath, catalogXml, new UTF8Encoding(false));
                File.Copy(temporaryPath, cachePath, true);
                File.Delete(temporaryPath);
            }
            catch (Exception exception)
            {
                Logger.Warning("[ModPortal] Could not cache portal catalog: " + exception.Message);
            }
        }

        private static void SaveXml(string path, XDocument document)
        {
            string temporaryPath = path + ".tmp";
            document.Save(temporaryPath);
            File.Copy(temporaryPath, path, true);
            File.Delete(temporaryPath);
        }

        private static XDocument LoadXml(string xml)
        {
            XmlReaderSettings settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit
            };
            using (StringReader stringReader = new StringReader(xml))
            using (XmlReader reader = XmlReader.Create(stringReader, settings))
            {
                return XDocument.Load(reader);
            }
        }

        private static WebClient CreateWebClient()
        {
            WebClient client = new WebClient();
            client.Headers[HttpRequestHeader.UserAgent] = "LizziesModPortal/1";
            return client;
        }

        private static string ComputeSha256(byte[] content)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(content);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                }

                return builder.ToString();
            }
        }

        private static string GetRequiredAttribute(XElement element, string attributeName)
        {
            string value = GetOptionalAttribute(element, attributeName);
            if (string.IsNullOrEmpty(value)) throw new InvalidOperationException("A '" + attributeName + "' attribute is required.");
            return value;
        }

        private static string GetOptionalAttribute(XElement element, string attributeName)
        {
            return ((string)element.Attribute(attributeName) ?? "").Trim();
        }

        private static string NormalizeRelativePath(string path)
        {
            return (path ?? "").Replace('\\', '/').TrimStart('/').Trim();
        }

        private static string GetSettingsPath()
        {
            return string.IsNullOrEmpty(coreModPath) ? "" : Path.Combine(coreModPath, SettingsFileName);
        }

        private static string GetCachePath()
        {
            return string.IsNullOrEmpty(coreModPath) ? "" : Path.Combine(coreModPath, CatalogCacheFileName);
        }

        private static void SetStatus(string message)
        {
            lock (Sync)
            {
                status = message;
            }
        }

        private static string GetDisplayName(ModPortalPackage package)
        {
            return string.IsNullOrEmpty(package.DisplayName) ? package.Id : package.DisplayName;
        }

        private static bool PathsEqual(string firstPath, string secondPath)
        {
            return string.Equals(
                Path.GetFullPath(firstPath).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(secondPath).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static ModPortalPackage ClonePackage(ModPortalPackage package)
        {
            ModPortalPackage clone = new ModPortalPackage
            {
                Id = package.Id,
                DisplayName = package.DisplayName,
                Version = package.Version,
                Description = package.Description,
                Author = package.Author,
                Website = package.Website,
                GameVersion = package.GameVersion
            };
            foreach (ModPortalFile file in package.Files)
            {
                clone.Files.Add(new ModPortalFile
                {
                    RelativePath = file.RelativePath,
                    Url = file.Url,
                    Sha256 = file.Sha256,
                    Size = file.Size
                });
            }

            return clone;
        }
    }
}