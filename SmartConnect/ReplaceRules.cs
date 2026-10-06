using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace SmartConnect
{
    /// <summary>
    /// Règle « jonction contre segment ».
    ///
    /// Une tuile existante traversée par le glissé est remplacée (comportement vanilla) seulement si
    /// l'une de ses voisines dans le tracé est aussi une tuile existante à laquelle elle était déjà
    /// connectée avant le glissé : le joueur suit la ligne, il veut la refaire. Sinon (bout de ligne,
    /// branchement en T, croisement, deux lignes voisines non reliées) elle est protégée : elle garde
    /// son matériau et son type, et reçoit seulement la nouvelle connexion.
    /// Un tracé d'une seule case reste vanilla (clic explicite sur la tuile).
    ///
    /// Les connexions « avant le glissé » sont lues dans la grille physique du gestionnaire de réseau :
    /// le glissé ne modifie que la grille visuelle tant qu'il n'est pas validé.
    /// </summary>
    public static class ReplaceRules
    {
        private static readonly FieldInfo PathField =
            AccessTools.Field(typeof(BaseUtilityBuildTool), "path");
        private static readonly System.Type PathNodeType =
            AccessTools.Inner(typeof(BaseUtilityBuildTool), "PathNode");
        private static readonly FieldInfo NodeCellField = AccessTools.Field(PathNodeType, "cell");
        private static readonly FieldInfo NodeVisualizerField = AccessTools.Field(PathNodeType, "visualizer");

        private static readonly AccessTools.FieldRef<BaseUtilityBuildTool, BuildingDef> DefRef =
            AccessTools.FieldRefAccess<BaseUtilityBuildTool, BuildingDef>("def");
        private static readonly AccessTools.FieldRef<BaseUtilityBuildTool, IList<Tag>> SelectedElementsRef =
            AccessTools.FieldRefAccess<BaseUtilityBuildTool, IList<Tag>>("selectedElements");
        private static readonly AccessTools.FieldRef<BaseUtilityBuildTool, IUtilityNetworkMgr> ConduitMgrRef =
            AccessTools.FieldRefAccess<BaseUtilityBuildTool, IUtilityNetworkMgr>("conduitMgr");

        // Réutilisés à chaque frame de l'aperçu pour ne rien allouer.
        private static readonly List<int> cells = new List<int>();
        private static readonly List<UnityEngine.GameObject> visualizers = new List<UnityEngine.GameObject>();
        private static readonly HashSet<int> protectedCells = new HashSet<int>();

        // Décalage vers la caméra des aperçus teintés (les objets OverlayInFrontOfConduits sont à 0,2).
        private const float PreviewZOffset = 0.05f;

        private static bool buildActive;
        private static bool previewErrorLogged;

        /// <summary>Prefix de BuildPath : calcule les tuiles protégées avant toute modification.</summary>
        internal static void BeginBuild(BaseUtilityBuildTool tool)
        {
            try
            {
                ReadPath(tool);
                ComputeProtected(DefRef(tool), ConduitMgrRef(tool));
                buildActive = true;
            }
            catch (System.Exception e)
            {
                // En cas de problème : aucune protection, le jeu garde son comportement vanilla.
                protectedCells.Clear();
                buildActive = false;
                Debug.LogWarning("[SmartConnect] BuildPath prefix failed, vanilla behaviour used: " + e);
            }
        }

        /// <summary>Finalizer de BuildPath.</summary>
        internal static void EndBuild()
        {
            buildActive = false;
            protectedCells.Clear();
        }

        /// <summary>
        /// Remplace l'appel à BuildingDef.IsValidBuildLocation qui ouvre le bloc de remplacement de
        /// BuildPath (voir le transpiler). Renvoyer false saute ce bloc : pas de chantier de
        /// remplacement, la connexion a déjà été appliquée à la tuile existante juste avant.
        /// </summary>
        public static bool IsValidReplaceLocation(BuildingDef def, UnityEngine.GameObject source_go,
            UnityEngine.Vector3 pos, Orientation orientation, bool replace_tile)
        {
            if (!def.IsValidBuildLocation(source_go, pos, orientation, replace_tile))
                return false;
            return !(buildActive && protectedCells.Contains(Grid.PosToCell(pos)));
        }

        /// <summary>
        /// Postfix de chaque frame de VisUpdater : teinte les aperçus posés sur des tuiles existantes.
        /// Les tuiles neuves gardent le blanc vanilla, les emplacements invalides gardent le rouge.
        /// </summary>
        internal static void ApplyPreview(BaseUtilityBuildTool tool)
        {
            try
            {
                BuildingDef def = DefRef(tool);
                if (def == null || def.ReplacementLayer == ObjectLayer.NumLayers)
                    return;
                ReadPath(tool);
                ComputeProtected(def, ConduitMgrRef(tool));
                IList<Tag> selected = SelectedElementsRef(tool);
                SmartConnectOptions settings = SmartConnectMod.Settings;

                for (int i = 0; i < cells.Count; i++)
                {
                    UnityEngine.GameObject visualizer = visualizers[i];
                    if (visualizer == null || GetExisting(cells[i], def) == null)
                        continue;
                    KBatchedAnimController anim = visualizer.GetComponent<KBatchedAnimController>();
                    if (anim == null)
                        continue;
                    UnityEngine.Color32 tint = anim.TintColour;
                    if (tint.r == 255 && tint.g == 0 && tint.b == 0)
                        continue;
                    BringToFront(visualizer, anim, def);
                    bool rebuilt = VanillaWouldReplace(def, selected, cells[i]) && !protectedCells.Contains(cells[i]);
                    anim.TintColour = rebuilt ? settings.ReplaceColor : settings.KeepColor;
                }
            }
            catch (System.Exception e)
            {
                if (!previewErrorLogged)
                {
                    previewErrorLogged = true;
                    Debug.LogWarning("[SmartConnect] preview failed: " + e);
                }
            }
        }

        /// <summary>
        /// En vue réseau, les tuyaux existants passent dans la caméra de l'aperçu (couche MaskedOverlay)
        /// à la même profondeur que lui : l'ordre de dessin devient aléatoire et le tuyau existant peut
        /// recouvrir la teinte. On avance l'aperçu teinté d'un cheveu, puis on le réenregistre dans son
        /// lot de rendu (même astuce que OverlayModes.TriggerResorting).
        /// </summary>
        private static void BringToFront(UnityEngine.GameObject visualizer, KBatchedAnimController anim, BuildingDef def)
        {
            UnityEngine.Vector3 pos = visualizer.transform.position;
            float z = Grid.GetLayerZ(def.SceneLayer) - PreviewZOffset;
            if (pos.z == z || !anim.enabled)
                return;
            pos.z = z;
            visualizer.transform.position = pos;
            anim.enabled = false;
            anim.enabled = true;
        }

        private static void ReadPath(BaseUtilityBuildTool tool)
        {
            cells.Clear();
            visualizers.Clear();
            if (!(PathField.GetValue(tool) is IList path))
                return;
            for (int i = 0; i < path.Count; i++)
            {
                object node = path[i];
                cells.Add((int)NodeCellField.GetValue(node));
                visualizers.Add(NodeVisualizerField.GetValue(node) as UnityEngine.GameObject);
            }
        }

        private static void ComputeProtected(BuildingDef def, IUtilityNetworkMgr mgr)
        {
            protectedCells.Clear();
            int count = cells.Count;
            if (count < 2 || def == null || mgr == null)
                return;
            bool protectOtherTypes = SmartConnectMod.Settings.ProtectOtherTypes;
            for (int i = 0; i < count; i++)
            {
                int cell = cells[i];
                BuildingComplete existing = GetExisting(cell, def);
                if (existing == null)
                    continue;
                if (!protectOtherTypes && existing.Def != def)
                    continue;
                bool followsLine = (i > 0 && AlreadyConnected(cell, cells[i - 1], def, mgr))
                                   || (i + 1 < count && AlreadyConnected(cell, cells[i + 1], def, mgr));
                if (!followsLine)
                    protectedCells.Add(cell);
            }
        }

        /// <summary>Vrai si les deux cellules portent une tuile existante et étaient reliées avant le glissé.</summary>
        private static bool AlreadyConnected(int cell, int other, BuildingDef def, IUtilityNetworkMgr mgr)
        {
            if (GetExisting(other, def) == null)
                return false;
            UtilityConnections direction = UtilityConnectionsExtensions.DirectionFromToCell(cell, other);
            if (direction == 0)
                return false;
            return (mgr.GetConnections(cell, is_physical_building: true) & direction) != 0
                   && (mgr.GetConnections(other, is_physical_building: true) & direction.InverseDirection()) != 0;
        }

        private static BuildingComplete GetExisting(int cell, BuildingDef def)
        {
            if (!Grid.IsValidCell(cell))
                return null;
            UnityEngine.GameObject go = Grid.Objects[cell, (int)def.TileLayer];
            return go != null ? go.GetComponent<BuildingComplete>() : null;
        }

        /// <summary>Mêmes conditions que le bloc de remplacement de BaseUtilityBuildTool.BuildPath.</summary>
        private static bool VanillaWouldReplace(BuildingDef def, IList<Tag> selected, int cell)
        {
            if (DebugHandler.InstantBuildMode)
                return false;
            if (Game.Instance.SandboxModeActive && SandboxToolParameterMenu.instance != null
                && SandboxToolParameterMenu.instance.settings.InstantBuild)
                return false;
            UnityEngine.Vector3 pos = Grid.CellToPosCBC(cell, Grid.SceneLayer.Building);
            if (!def.IsValidBuildLocation(null, pos, Orientation.Neutral))
                return false;
            if (Grid.Objects[cell, (int)def.ReplacementLayer] != null)
                return false;
            BuildingComplete existing = GetExisting(cell, def);
            if (existing == null)
                return false;
            if (existing.Def != def)
                return true;
            PrimaryElement element = existing.GetComponent<PrimaryElement>();
            return element != null && selected != null && selected.Count > 0
                   && element.Element.tag != selected[0];
        }
    }
}
