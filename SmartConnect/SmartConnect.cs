// Smart Connect — mod Oxygen Not Included
//
// Vanilla: when a pipe / conveyor rail / wire drag touches an existing tile of another material
// (or another type: insulated, radiant, conductive...), BaseUtilityBuildTool.BuildPath queues a
// rebuild of that tile, even when the player only wanted to connect to it.
//
// This mod keeps an existing tile ("protected": it only gets the new connection) unless the drag
// follows the existing line, i.e. unless one of its neighbours in the drag is an existing tile it
// was already connected to before the drag. A single click on one tile still replaces it.
// While dragging, existing tiles under the drag are tinted: one color if they will be rebuilt,
// another if they will be kept.
//
// Tous les types System/Unity sont qualifiés : Assembly-CSharp d'ONI déclare des types
// dans le namespace global (Debug…) qui masquent ceux importés par « using ».

using HarmonyLib;
using Newtonsoft.Json;
using PeterHan.PLib.Core;
using PeterHan.PLib.Options;

namespace SmartConnect
{
    public sealed class SmartConnectMod : KMod.UserMod2
    {
        internal static SmartConnectOptions Settings = new SmartConnectOptions();

        public override void OnLoad(Harmony harmony)
        {
            PUtil.InitLibrary(false);
            new POptions().RegisterOptions(this, typeof(SmartConnectOptions));
            Settings = POptions.ReadSettings<SmartConnectOptions>() ?? new SmartConnectOptions();

            base.OnLoad(harmony);

            Debug.Log("[SmartConnect] loaded — protect other types " + Settings.ProtectOtherTypes
                      + ", preview " + Settings.ShowPreview);
        }
    }

    /// <summary>Options shown in the Mods menu (PLib). Applied immediately, no restart needed.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SmartConnectOptions : IOptions
    {
        [Option("Protect other pipe and wire types",
            "When a line is connected to an existing tile of another type\n" +
            "(insulated or radiant pipe, conductive or Heavi-Watt wire, ribbon...),\n" +
            "keep that tile as it is instead of rebuilding it with the new type.\n" +
            "Off: only tiles of the same type with another material are protected.",
            "Replacement")]
        [JsonProperty]
        public bool ProtectOtherTypes { get; set; }

        [Option("Show preview colors",
            "While dragging, tint the existing tiles crossed by the drag:\n" +
            "one color if they will be rebuilt, another if they will be kept.",
            "Preview")]
        [JsonProperty]
        public bool ShowPreview { get; set; }

        [Option("Rebuilt tile color",
            "Tint of existing tiles that will be rebuilt with the new material or type.",
            "Preview")]
        [JsonProperty]
        public UnityEngine.Color32 ReplaceColor { get; set; }

        [Option("Kept tile color",
            "Tint of existing tiles that will be kept and only connected.",
            "Preview")]
        [JsonProperty]
        public UnityEngine.Color32 KeepColor { get; set; }

        public SmartConnectOptions()
        {
            ProtectOtherTypes = true;
            ShowPreview = true;
            ReplaceColor = new UnityEngine.Color32(255, 140, 30, 255);
            KeepColor = new UnityEngine.Color32(70, 200, 255, 255);
        }

        public System.Collections.Generic.IEnumerable<IOptionsEntry> CreateOptions()
        {
            return System.Array.Empty<IOptionsEntry>();
        }

        /// <summary>Appelé par PLib après l'écriture du fichier : les options s'appliquent tout de suite.</summary>
        public void OnOptionsChanged()
        {
            SmartConnectMod.Settings = this;
        }
    }
}
