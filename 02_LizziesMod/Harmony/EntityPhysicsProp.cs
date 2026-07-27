using System.Collections.Generic;
using UnityEngine;

namespace LizziesMod
{
    public class EntityPhysicsProp : EntityFallingBlock
    {
        private const int MeshCollisionInitializationAttempts = 60;
        private bool meshCollisionConfigured;
        private int meshCollisionAttempts;

        public override void OnUpdateEntity()
        {
            // EntityFallingBlock applies impact damage and turns stationary blocks back into world blocks.
            // Entity.Update still owns physics and network movement before this hook is called.
            firstUpdate = false;
            ConfigureMeshCollision();
        }

        private void ConfigureMeshCollision()
        {
            if (meshCollisionConfigured || meshCollisionAttempts >= MeshCollisionInitializationAttempts) return;

            meshCollisionAttempts++;
            if (ModelTransform == null) return;

            MeshFilter[] meshFilters = ModelTransform.GetComponentsInChildren<MeshFilter>(true);
            List<MeshCollider> meshColliders = new List<MeshCollider>();

            foreach (MeshFilter meshFilter in meshFilters)
            {
                if (meshFilter == null || meshFilter.sharedMesh == null || meshFilter.sharedMesh.vertexCount == 0) continue;

                MeshCollider meshCollider = meshFilter.GetComponent<MeshCollider>();
                if (meshCollider == null)
                {
                    meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();
                }

                meshCollider.sharedMesh = meshFilter.sharedMesh;
                meshCollider.convex = true;
                meshCollider.isTrigger = false;
                meshCollider.enabled = true;
                meshColliders.Add(meshCollider);
            }

            if (meshColliders.Count == 0) return;

            foreach (Collider collider in GetComponentsInChildren<Collider>(true))
            {
                if (collider == null || collider.isTrigger || meshColliders.Contains(collider as MeshCollider)) continue;

                collider.enabled = false;
            }

            Physics.SyncTransforms();
            meshCollisionConfigured = true;
            Logger.Info($"[PropSpawner] Configured {meshColliders.Count} mesh collider(s) for prop {entityId}.");
        }
    }
}