using System;
using System.Collections.Generic;
using UnityEngine;

namespace NW.FairyToon.Editor
{
    /// <summary>Optional overrides applied after lilToon variant mapping and heuristics.</summary>
    [CreateAssetMenu(menuName = "FairyToon/Convert Profile")]
    public sealed class FairyToonConvertProfile : ScriptableObject
    {
        public enum Kind { Float, Color, Vector, Texture }

        [Serializable]
        public sealed class Override
        {
            public string property;
            public Kind kind;
            public float floatValue;
            public Color colorValue = Color.white;
            public Vector4 vectorValue;
            public Texture textureValue;
        }

        [Serializable]
        public sealed class Entry
        {
            public Material source;
            public List<Override> overrides = new List<Override>();
        }

        public List<Entry> entries = new List<Entry>();
    }

    /// <summary>Metal contrast style for Endfield material upgrades.</summary>
    public enum FairyToonEndfieldMetalStyle
    {
        /// <summary>Infer BrightSilver vs SmokedGunmetal from source MatCap blend mode and tint.</summary>
        Auto = 0,
        /// <summary>High-contrast mirror chrome / sterling silver hardware.</summary>
        BrightSilver = 1,
        /// <summary>Dark smoked titanium / brushed gunmetal hardware.</summary>
        SmokedGunmetal = 2
    }

    /// <summary>Shared options for a single conversion or a batch.</summary>
    [Serializable]
    public sealed class FairyToonConvertOptions
    {
        public string OutputFolder;
        public bool InPlace;
        public bool TransparentHairToCutout = true;
        public FairyToonConvertProfile Profile;
        public bool UseProjectProfiles = true;

        /// <summary>Upgrade Opaque/Cutout/Transparent materials to Endfield 0.3.0 PBR while preserving authored lilToon intent.</summary>
        public bool UpgradeToEndfield;
        /// <summary>Pack a dedicated Endfield _PBRMap PNG when converting persisted Assets materials.</summary>
        public bool PackPBRMaps = true;
        /// <summary>Optional Assets/... folder for packed _PBRMap textures (defaults to &lt;target folder&gt;/PBRMaps).</summary>
        public string PBRMapOutputFolder;
        /// <summary>Optional Endfield diffuse ramp assigned during Endfield upgrade.</summary>
        public Texture2D EndfieldDiffuseRamp;
        /// <summary>Optional Endfield specular ramp assigned during Endfield upgrade.</summary>
        public Texture2D EndfieldSpecRamp;
        /// <summary>Metal grazing/contrast calibration strategy when UpgradeToEndfield is enabled.</summary>
        public FairyToonEndfieldMetalStyle MetalStyle = FairyToonEndfieldMetalStyle.Auto;
        /// <summary>Gently boost authored HDR Glitter intensity while keeping per-material particle size and density.</summary>
        public bool BoostGlitterHDR = true;
    }
}
