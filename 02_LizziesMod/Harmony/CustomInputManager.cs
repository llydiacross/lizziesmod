using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using UnityEngine;

namespace LizziesMod
{
    public enum CustomInputTrigger
    {
        Pressed,
        Released,
        Held
    }

    public sealed class CustomInputDefinition
    {
        public string Id { get; private set; }
        public string ModName { get; private set; }
        public string Name { get; private set; }
        public string Category { get; private set; }
        public string Description { get; private set; }
        public string DefaultChord { get; private set; }
        public string Chord { get; private set; }

        internal List<List<KeyCode>> KeyGroups { get; private set; }
        internal bool IsHeld;
        internal bool WasPressed;
        internal bool WasReleased;

        internal CustomInputDefinition(
            string modName,
            string name,
            string category,
            string description,
            string chord,
            List<List<KeyCode>> keyGroups)
        {
            ModName = modName;
            Name = name;
            Category = category;
            Description = description;
            Id = CustomInputManager.GetInputId(modName, name);
            DefaultChord = chord;
            SetChord(chord, keyGroups);
        }

        internal void SetChord(string chord, List<List<KeyCode>> keyGroups)
        {
            Chord = chord;
            KeyGroups = keyGroups;
        }
    }

    public class CustomInputManager : MonoBehaviour
    {
        private const string OverrideDirectoryName = "LizziesMod";
        private const string OverrideFileName = "CustomInputOverrides.xml";
        private static readonly Dictionary<string, CustomInputDefinition> inputsById =
            new Dictionary<string, CustomInputDefinition>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Dictionary<CustomInputTrigger, List<Action>>> handlersByInputId =
            new Dictionary<string, Dictionary<CustomInputTrigger, List<Action>>>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> overridesByInputId =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly KeyCode[] allKeyCodes = (KeyCode[])Enum.GetValues(typeof(KeyCode));
        private static bool isInitialized;
        private static string capturedInputId;
        private static int captureStartFrame;
        private static bool suppressNextInputDispatch;
        private static KeyCode pendingPointerKey = KeyCode.None;
        private static string pendingPointerInputId;
        private static string pendingPointerChord;
        private static int pendingPointerReleaseFrame = -1;

        public static bool IsCapturing { get { return !string.IsNullOrEmpty(capturedInputId); } }
        public static string CapturedInputId { get { return capturedInputId ?? ""; } }

        public static void Initialize()
        {
            if (isInitialized) return;

            isInitialized = true;
            ReloadInputs();

            GameObject inputObject = new GameObject("LizziesCustomInput");
            DontDestroyOnLoad(inputObject);
            inputObject.AddComponent<CustomInputManager>();
        }

        public static void ReloadInputs()
        {
            CancelRebind();
            LoadOverrides();
            inputsById.Clear();
            int loadedCount = 0;

            foreach (Mod mod in global::ModManager.GetLoadedMods())
            {
                loadedCount += LoadInputsFromMod(mod);
            }

            Logger.Info($"[CustomInput] Loaded {loadedCount} input binding(s).");
        }

        public static string GetInputId(string modName, string inputName)
        {
            return (modName ?? "").Trim() + "." + (inputName ?? "").Trim();
        }

        public static bool HasInput(string modName, string inputName)
        {
            return inputsById.ContainsKey(GetInputId(modName, inputName));
        }

        public static CustomInputDefinition GetInput(string inputId)
        {
            CustomInputDefinition definition;
            return inputsById.TryGetValue(inputId ?? "", out definition) ? definition : null;
        }

        public static List<CustomInputDefinition> GetInputsForMod(string modName)
        {
            List<CustomInputDefinition> result = new List<CustomInputDefinition>();
            foreach (CustomInputDefinition definition in inputsById.Values)
            {
                if (definition.ModName.Equals(modName ?? "", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(definition);
                }
            }

            result.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        public static bool BeginRebind(string inputId)
        {
            if (!inputsById.ContainsKey(inputId ?? "")) return false;

            capturedInputId = inputId;
            captureStartFrame = Time.frameCount;
            suppressNextInputDispatch = true;
            ClearPendingPointerCapture();
            ClearInputTransitions();
            return true;
        }

        public static void CancelRebind()
        {
            if (!IsCapturing) return;

            capturedInputId = null;
            suppressNextInputDispatch = true;
            ClearPendingPointerCapture();
            ClearInputTransitions();
        }

        public static bool TryRebind(string inputId, string chord, out string error)
        {
            error = null;
            CustomInputDefinition definition;
            if (!inputsById.TryGetValue(inputId ?? "", out definition))
            {
                error = "The selected input no longer exists.";
                return false;
            }

            List<List<KeyCode>> keyGroups;
            if (!TryParseChord(chord, out keyGroups, out error)) return false;

            string normalizedChord = BuildChord(keyGroups);
            definition.SetChord(normalizedChord, keyGroups);
            overridesByInputId[inputId] = normalizedChord;
            SaveOverrides();
            ReportRuntimeChordConflict(definition);
            return true;
        }

        public static bool RestoreDefault(string inputId)
        {
            CustomInputDefinition definition;
            if (!inputsById.TryGetValue(inputId ?? "", out definition)) return false;

            List<List<KeyCode>> keyGroups;
            string error;
            if (!TryParseChord(definition.DefaultChord, out keyGroups, out error))
            {
                Logger.Error($"[CustomInput] Cannot restore default for '{inputId}': {error}");
                return false;
            }

            definition.SetChord(BuildChord(keyGroups), keyGroups);
            overridesByInputId.Remove(inputId);
            SaveOverrides();
            return true;
        }

        public static void Subscribe(string modName, string inputName, CustomInputTrigger trigger, Action handler)
        {
            Subscribe(GetInputId(modName, inputName), trigger, handler);
        }

        public static void Subscribe(string inputId, CustomInputTrigger trigger, Action handler)
        {
            if (string.IsNullOrEmpty(inputId) || handler == null) return;

            Dictionary<CustomInputTrigger, List<Action>> handlersByTrigger;
            if (!handlersByInputId.TryGetValue(inputId, out handlersByTrigger))
            {
                handlersByTrigger = new Dictionary<CustomInputTrigger, List<Action>>();
                handlersByInputId.Add(inputId, handlersByTrigger);
            }

            List<Action> handlers;
            if (!handlersByTrigger.TryGetValue(trigger, out handlers))
            {
                handlers = new List<Action>();
                handlersByTrigger.Add(trigger, handlers);
            }

            if (!handlers.Contains(handler)) handlers.Add(handler);
        }

        public static void Unsubscribe(string modName, string inputName, CustomInputTrigger trigger, Action handler)
        {
            Unsubscribe(GetInputId(modName, inputName), trigger, handler);
        }

        public static void Unsubscribe(string inputId, CustomInputTrigger trigger, Action handler)
        {
            if (string.IsNullOrEmpty(inputId) || handler == null) return;

            Dictionary<CustomInputTrigger, List<Action>> handlersByTrigger;
            if (!handlersByInputId.TryGetValue(inputId, out handlersByTrigger)) return;

            List<Action> handlers;
            if (!handlersByTrigger.TryGetValue(trigger, out handlers)) return;

            handlers.Remove(handler);
            if (handlers.Count == 0) handlersByTrigger.Remove(trigger);
            if (handlersByTrigger.Count == 0) handlersByInputId.Remove(inputId);
        }

        public static bool WasTriggered(string modName, string inputName, CustomInputTrigger trigger)
        {
            return WasTriggered(GetInputId(modName, inputName), trigger);
        }

        public static bool WasTriggered(string inputId, CustomInputTrigger trigger)
        {
            CustomInputDefinition definition;
            if (!inputsById.TryGetValue(inputId ?? "", out definition)) return false;

            if (trigger == CustomInputTrigger.Pressed) return definition.WasPressed;
            if (trigger == CustomInputTrigger.Released) return definition.WasReleased;
            return definition.IsHeld;
        }

        public static bool IsHeld(string modName, string inputName)
        {
            return IsHeld(GetInputId(modName, inputName));
        }

        public static bool IsHeld(string inputId)
        {
            CustomInputDefinition definition;
            return inputsById.TryGetValue(inputId ?? "", out definition) && definition.IsHeld;
        }

        public static List<CustomInputDefinition> GetInputs(string category = null)
        {
            List<CustomInputDefinition> result = new List<CustomInputDefinition>();
            foreach (CustomInputDefinition definition in inputsById.Values)
            {
                if (!string.IsNullOrEmpty(category) &&
                    !definition.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(definition);
            }

            result.Sort((left, right) => string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private void Update()
        {
            if (IsCapturing)
            {
                UpdateCapture();
                return;
            }

            if (suppressNextInputDispatch)
            {
                SynchronizeInputStates();
                suppressNextInputDispatch = false;
                return;
            }

            List<CustomInputDefinition> definitions = new List<CustomInputDefinition>(inputsById.Values);
            foreach (CustomInputDefinition definition in definitions)
            {
                bool wasHeld = definition.IsHeld;
                definition.IsHeld = IsChordHeld(definition);
                definition.WasPressed = !wasHeld && definition.IsHeld;
                definition.WasReleased = wasHeld && !definition.IsHeld;

                if (definition.WasPressed) Dispatch(definition.Id, CustomInputTrigger.Pressed);
                if (definition.WasReleased) Dispatch(definition.Id, CustomInputTrigger.Released);
                if (definition.IsHeld) Dispatch(definition.Id, CustomInputTrigger.Held);
            }
        }

        private static int LoadInputsFromMod(Mod mod)
        {
            if (mod == null || string.IsNullOrEmpty(mod.Path)) return 0;

            string configPath = Path.Combine(mod.Path, "Config", "CustomInput.xml");
            if (!File.Exists(configPath)) return 0;

            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(configPath);
                if (document.DocumentElement == null || document.DocumentElement.Name != "CustomInputs")
                {
                    ReportXmlError(mod.Name, "CustomInput.xml must use <CustomInputs> as its root element.");
                    return 0;
                }

                XmlNodeList inputNodes = document.SelectNodes("/CustomInputs/Input");
                if (inputNodes == null) return 0;

                int loadedCount = 0;
                foreach (XmlNode inputNode in inputNodes)
                {
                    CustomInputDefinition definition;
                    string error;
                    if (!TryCreateDefinition(mod.Name, inputNode, out definition, out error))
                    {
                        ReportXmlError(mod.Name, "CustomInput.xml: " + error);
                        continue;
                    }

                    if (inputsById.ContainsKey(definition.Id))
                    {
                        ReportXmlError(mod.Name, $"CustomInput.xml: input '{definition.Name}' is declared more than once.");
                        continue;
                    }

                    ApplyOverride(definition);
                    ReportChordConflict(definition);
                    inputsById.Add(definition.Id, definition);
                    loadedCount++;
                }

                return loadedCount;
            }
            catch (Exception exception)
            {
                ReportXmlError(mod.Name, "CustomInput.xml: " + exception.Message);
                return 0;
            }
        }

        private static bool TryCreateDefinition(
            string modName,
            XmlNode inputNode,
            out CustomInputDefinition definition,
            out string error)
        {
            definition = null;
            error = null;

            string name = inputNode.Attributes?["name"]?.Value?.Trim();
            string category = inputNode.Attributes?["category"]?.Value?.Trim();
            string description = inputNode.Attributes?["description"]?.Value?.Trim();
            string chord = inputNode.Attributes?["keys"]?.Value?.Trim();

            if (string.IsNullOrEmpty(name))
            {
                error = "each <Input> requires a non-empty name attribute.";
                return false;
            }

            if (string.IsNullOrEmpty(category))
            {
                error = $"input '{name}' requires a non-empty category attribute.";
                return false;
            }

            if (string.IsNullOrEmpty(description))
            {
                error = $"input '{name}' requires a non-empty description attribute.";
                return false;
            }

            List<List<KeyCode>> keyGroups;
            if (!TryParseChord(chord, out keyGroups, out error))
            {
                error = $"input '{name}' has an invalid keys attribute: {error}";
                return false;
            }

            definition = new CustomInputDefinition(modName, name, category, description, BuildChord(keyGroups), keyGroups);
            return true;
        }

        private static bool TryParseChord(string chord, out List<List<KeyCode>> keyGroups, out string error)
        {
            keyGroups = new List<List<KeyCode>>();
            error = null;

            if (string.IsNullOrEmpty(chord))
            {
                error = "a keys attribute is required.";
                return false;
            }

            string[] tokens = chord.Split('+');
            HashSet<string> seenTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawToken in tokens)
            {
                string token = rawToken.Trim();
                if (string.IsNullOrEmpty(token))
                {
                    error = "key combinations cannot contain empty parts.";
                    return false;
                }

                List<KeyCode> keyGroup;
                string normalizedToken;
                if (!TryParseKeyGroup(token, out keyGroup, out normalizedToken))
                {
                    error = $"'{token}' is not a recognized key or modifier.";
                    return false;
                }

                if (!seenTokens.Add(normalizedToken))
                {
                    error = $"'{token}' is declared more than once.";
                    return false;
                }

                keyGroups.Add(keyGroup);
            }

            return keyGroups.Count > 0;
        }

        private static bool TryParseKeyGroup(string token, out List<KeyCode> keyGroup, out string normalizedToken)
        {
            keyGroup = null;
            normalizedToken = null;

            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                keyGroup = new List<KeyCode> { KeyCode.LeftControl, KeyCode.RightControl };
                normalizedToken = "Ctrl";
                return true;
            }

            if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                keyGroup = new List<KeyCode> { KeyCode.LeftShift, KeyCode.RightShift };
                normalizedToken = "Shift";
                return true;
            }

            if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                keyGroup = new List<KeyCode> { KeyCode.LeftAlt, KeyCode.RightAlt };
                normalizedToken = "Alt";
                return true;
            }

            KeyCode keyCode;
            if (!Enum.TryParse(token, true, out keyCode) || keyCode == KeyCode.None)
            {
                return false;
            }

            keyGroup = new List<KeyCode> { keyCode };
            normalizedToken = keyCode.ToString();
            return true;
        }

        private static bool IsChordHeld(CustomInputDefinition definition)
        {
            foreach (List<KeyCode> keyGroup in definition.KeyGroups)
            {
                bool groupIsHeld = false;
                foreach (KeyCode keyCode in keyGroup)
                {
                    if (Input.GetKey(keyCode))
                    {
                        groupIsHeld = true;
                        break;
                    }
                }

                if (!groupIsHeld) return false;
            }

            return true;
        }

        private static void UpdateCapture()
        {
            if (Time.frameCount <= captureStartFrame) return;

            if (UpdatePendingPointerCapture()) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CancelRebind();
                return;
            }

            foreach (KeyCode keyCode in allKeyCodes)
            {
                if (!IsCapturablePrimaryKey(keyCode) || !Input.GetKeyDown(keyCode)) continue;

                string inputId = capturedInputId;
                string chord = BuildCapturedChord(keyCode);
                if (IsPointerKey(keyCode))
                {
                    pendingPointerKey = keyCode;
                    pendingPointerInputId = inputId;
                    pendingPointerChord = chord;
                    return;
                }

                CompleteCapture(inputId, chord);
                return;
            }
        }

        private static bool UpdatePendingPointerCapture()
        {
            if (pendingPointerKey == KeyCode.None) return false;

            if (!IsCapturing || !capturedInputId.Equals(pendingPointerInputId, StringComparison.OrdinalIgnoreCase))
            {
                ClearPendingPointerCapture();
                return true;
            }

            if (Input.GetKey(pendingPointerKey))
            {
                pendingPointerReleaseFrame = -1;
                return true;
            }

            if (pendingPointerReleaseFrame < 0)
            {
                pendingPointerReleaseFrame = Time.frameCount;
                return true;
            }

            if (Time.frameCount <= pendingPointerReleaseFrame + 1) return true;

            string inputId = pendingPointerInputId;
            string chord = pendingPointerChord;
            ClearPendingPointerCapture();
            CompleteCapture(inputId, chord);
            return true;
        }

        private static string BuildCapturedChord(KeyCode keyCode)
        {
            List<string> chordParts = new List<string>();
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) chordParts.Add("Ctrl");
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) chordParts.Add("Shift");
            if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) chordParts.Add("Alt");
            chordParts.Add(keyCode.ToString());
            return string.Join("+", chordParts);
        }

        private static void CompleteCapture(string inputId, string chord)
        {
            string error;
            if (!TryRebind(inputId, chord, out error))
            {
                Logger.Warning($"[CustomInput] Could not rebind '{inputId}': {error}");
            }

            capturedInputId = null;
            suppressNextInputDispatch = true;
            ClearInputTransitions();
        }

        private static void ClearPendingPointerCapture()
        {
            pendingPointerKey = KeyCode.None;
            pendingPointerInputId = null;
            pendingPointerChord = null;
            pendingPointerReleaseFrame = -1;
        }

        private static bool IsCapturablePrimaryKey(KeyCode keyCode)
        {
            return keyCode != KeyCode.None &&
                   keyCode != KeyCode.Escape &&
                   !IsModifierKey(keyCode);
        }

        private static bool IsModifierKey(KeyCode keyCode)
        {
            return keyCode == KeyCode.LeftControl ||
                   keyCode == KeyCode.RightControl ||
                   keyCode == KeyCode.LeftShift ||
                   keyCode == KeyCode.RightShift ||
                   keyCode == KeyCode.LeftAlt ||
                   keyCode == KeyCode.RightAlt;
        }

        private static bool IsPointerKey(KeyCode keyCode)
        {
            return keyCode == KeyCode.Mouse0 ||
                   keyCode == KeyCode.Mouse1 ||
                   keyCode == KeyCode.Mouse2 ||
                   keyCode == KeyCode.Mouse3 ||
                   keyCode == KeyCode.Mouse4 ||
                   keyCode == KeyCode.Mouse5 ||
                   keyCode == KeyCode.Mouse6;
        }

        private static string BuildChord(List<List<KeyCode>> keyGroups)
        {
            List<string> chordParts = new List<string>();
            foreach (List<KeyCode> keyGroup in keyGroups)
            {
                if (IsModifierGroup(keyGroup, KeyCode.LeftControl, KeyCode.RightControl)) chordParts.Add("Ctrl");
                else if (IsModifierGroup(keyGroup, KeyCode.LeftShift, KeyCode.RightShift)) chordParts.Add("Shift");
                else if (IsModifierGroup(keyGroup, KeyCode.LeftAlt, KeyCode.RightAlt)) chordParts.Add("Alt");
                else if (keyGroup.Count > 0) chordParts.Add(keyGroup[0].ToString());
            }

            return string.Join("+", chordParts);
        }

        private static bool IsModifierGroup(List<KeyCode> keyGroup, KeyCode leftKey, KeyCode rightKey)
        {
            return keyGroup.Count == 2 && keyGroup.Contains(leftKey) && keyGroup.Contains(rightKey);
        }

        private static void SynchronizeInputStates()
        {
            foreach (CustomInputDefinition definition in inputsById.Values)
            {
                definition.IsHeld = IsChordHeld(definition);
                definition.WasPressed = false;
                definition.WasReleased = false;
            }
        }

        private static void ClearInputTransitions()
        {
            foreach (CustomInputDefinition definition in inputsById.Values)
            {
                definition.WasPressed = false;
                definition.WasReleased = false;
            }
        }

        private static void ApplyOverride(CustomInputDefinition definition)
        {
            string overrideChord;
            if (!overridesByInputId.TryGetValue(definition.Id, out overrideChord)) return;

            List<List<KeyCode>> keyGroups;
            string error;
            if (!TryParseChord(overrideChord, out keyGroups, out error))
            {
                Logger.Warning($"[CustomInput] Ignoring invalid saved override for '{definition.Id}': {error}");
                return;
            }

            definition.SetChord(BuildChord(keyGroups), keyGroups);
        }

        private static string GetOverridePath()
        {
            return Path.Combine(Application.persistentDataPath, OverrideDirectoryName, OverrideFileName);
        }

        private static void LoadOverrides()
        {
            overridesByInputId.Clear();
            string overridePath = GetOverridePath();
            if (!File.Exists(overridePath)) return;

            try
            {
                XmlDocument document = new XmlDocument();
                document.Load(overridePath);
                if (document.DocumentElement == null || document.DocumentElement.Name != "CustomInputOverrides")
                {
                    Logger.Warning($"[CustomInput] Ignoring '{overridePath}': expected <CustomInputOverrides>.");
                    return;
                }

                XmlNodeList inputNodes = document.SelectNodes("/CustomInputOverrides/Input");
                if (inputNodes == null) return;

                foreach (XmlNode inputNode in inputNodes)
                {
                    string inputId = inputNode.Attributes?["id"]?.Value?.Trim();
                    string chord = inputNode.Attributes?["keys"]?.Value?.Trim();
                    List<List<KeyCode>> keyGroups;
                    string error;
                    if (string.IsNullOrEmpty(inputId) || !TryParseChord(chord, out keyGroups, out error))
                    {
                        Logger.Warning($"[CustomInput] Ignoring an invalid saved input override in '{overridePath}'.");
                        continue;
                    }

                    overridesByInputId[inputId] = BuildChord(keyGroups);
                }
            }
            catch (Exception exception)
            {
                Logger.Warning($"[CustomInput] Failed to load '{overridePath}': {exception.Message}");
            }
        }

        private static void SaveOverrides()
        {
            try
            {
                string overridePath = GetOverridePath();
                string overrideDirectory = Path.GetDirectoryName(overridePath);
                if (!string.IsNullOrEmpty(overrideDirectory)) Directory.CreateDirectory(overrideDirectory);

                List<string> inputIds = new List<string>(overridesByInputId.Keys);
                inputIds.Sort(StringComparer.OrdinalIgnoreCase);
                using (XmlWriter writer = XmlWriter.Create(overridePath, new XmlWriterSettings { Indent = true }))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("CustomInputOverrides");
                    foreach (string inputId in inputIds)
                    {
                        writer.WriteStartElement("Input");
                        writer.WriteAttributeString("id", inputId);
                        writer.WriteAttributeString("keys", overridesByInputId[inputId]);
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
            }
            catch (Exception exception)
            {
                Logger.Error($"[CustomInput] Failed to save input overrides: {exception.Message}");
            }
        }

        private static void Dispatch(string inputId, CustomInputTrigger trigger)
        {
            Dictionary<CustomInputTrigger, List<Action>> handlersByTrigger;
            if (!handlersByInputId.TryGetValue(inputId, out handlersByTrigger)) return;

            List<Action> handlers;
            if (!handlersByTrigger.TryGetValue(trigger, out handlers)) return;

            foreach (Action handler in new List<Action>(handlers))
            {
                try
                {
                    handler();
                }
                catch (Exception exception)
                {
                    Logger.Error($"[CustomInput] Handler for '{inputId}' failed: {exception.Message}");
                }
            }
        }

        private static void ReportChordConflict(CustomInputDefinition definition)
        {
            foreach (CustomInputDefinition existingDefinition in inputsById.Values)
            {
                if (!definition.Chord.Equals(existingDefinition.Chord, StringComparison.OrdinalIgnoreCase) ||
                    definition.ModName.Equals(existingDefinition.ModName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ModErrorHandler.ReportXmlWarning(
                    definition.ModName,
                    $"CustomInput.xml: input '{definition.Name}' shares '{definition.Chord}' with '{existingDefinition.Id}'.");
            }
        }

        private static void ReportRuntimeChordConflict(CustomInputDefinition definition)
        {
            foreach (CustomInputDefinition existingDefinition in inputsById.Values)
            {
                if (existingDefinition.Id.Equals(definition.Id, StringComparison.OrdinalIgnoreCase) ||
                    !definition.Chord.Equals(existingDefinition.Chord, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Logger.Warning($"[CustomInput] '{definition.Id}' shares '{definition.Chord}' with '{existingDefinition.Id}'.");
            }
        }

        private static void ReportXmlError(string modName, string message)
        {
            Logger.Error($"[CustomInput] [{modName}] {message}");
            ModErrorHandler.ReportXmlError(modName, message);
        }
    }
}