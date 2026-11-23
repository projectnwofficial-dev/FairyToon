using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NW.FairyToon.Editor
{
    /// <summary>See-through hair setup helpers: the URP renderer feature, material roles and renderer lookup.</summary>
    public static class FairyToonSeeThroughSetup
    {
        /// <summary>The renderer data URP draws the game with: the active quality level's asset, its default renderer.</summary>
        public static ScriptableRendererData ActiveRendererData()
        {
            var asset = (QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            if (asset == null) return null;
            var so = new SerializedObject(asset);
            var list = so.FindProperty("m_RendererDataList");
            int index = so.FindProperty("m_DefaultRendererIndex").intValue;
            if (list == null || index < 0 || index >= list.arraySize) return null;
            return list.GetArrayElementAtIndex(index).objectReferenceValue as ScriptableRendererData;
        }

        public static bool FeatureInstalled(ScriptableRendererData data = null)
        {
            data ??= ActiveRendererData();
            if (data == null) return false;
            foreach (var feature in data.rendererFeatures)
                if (feature is FairyToonFeature && feature.isActive) return true;
            return false;
        }

        /// <summary>Adds a FairyToon feature to the renderer data (the active one when null) as a sub-asset, like
        /// the renderer's own "Add Renderer Feature" button does. Returns false when one is already there.</summary>
        public static bool InstallFeature(ScriptableRendererData data = null)
        {
            data ??= ActiveRendererData();
            if (data == null) { Debug.LogWarning("[FairyToon] No URP renderer data found."); return false; }
            foreach (var existing in data.rendererFeatures)
                if (existing is FairyToonFeature) { if (!existing.isActive) existing.SetActive(true); return false; }

            var feature = ScriptableObject.CreateInstance<FairyToonFeature>();
            feature.name = "FairyToon";
            Undo.RegisterCreatedObjectUndo(feature, "Add FairyToon Feature");
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId);

            var so = new SerializedObject(data);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedProperties();
            data.SetDirty();
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
            Debug.Log($"[FairyToon] Added the FairyToon renderer feature to {AssetDatabase.GetAssetPath(data)}.");
            return true;
        }

        /// <summary>Renderer slots in the loaded scenes that use <paramref name="material"/>; the selection comes first.</summary>
        public static List<(Renderer renderer, int slot)> RenderersUsing(Material material)
        {
            var result = new List<(Renderer, int)>();
            var materials = new List<Material>();
            void Scan(IEnumerable<Renderer> renderers)
            {
                foreach (var renderer in renderers)
                {
                    renderer.GetSharedMaterials(materials);
                    for (int i = 0; i < materials.Count; i++)
                        if (materials[i] == material && !result.Contains((renderer, i))) result.Add((renderer, i));
                }
            }
            if (Selection.activeGameObject != null) Scan(Selection.activeGameObject.GetComponentsInChildren<Renderer>(true));
            Scan(Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include));
            return result;
        }
    }
}
