using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace SmartConnect
{
    /// <summary>
    /// BaseUtilityBuildTool.BuildPath sert aux deux outils de lignes : UtilityBuildTool (tuyaux
    /// liquide et gaz, rails) et WireBuildTool (fils électriques et d'automatisation).
    ///
    /// Dans la boucle sur le tracé, le bloc de remplacement commence par :
    ///   if (def.ReplacementLayer != ObjectLayer.NumLayers && !InstantBuild... && def.IsValidBuildLocation(null, vector, Orientation.Neutral))
    /// Le transpiler remplace cet appel à IsValidBuildLocation (le premier après la lecture de
    /// ReplacementLayer) par ReplaceRules.IsValidReplaceLocation, qui renvoie false pour les tuiles
    /// protégées. Repérage par la méthode appelée, sans index de variable locale codé en dur.
    /// </summary>
    [HarmonyPatch(typeof(BaseUtilityBuildTool), "BuildPath")]
    internal static class BaseUtilityBuildTool_BuildPath_Patch
    {
        internal static void Prefix(BaseUtilityBuildTool __instance)
        {
            ReplaceRules.BeginBuild(__instance);
        }

        internal static void Finalizer()
        {
            ReplaceRules.EndBuild();
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo replacementLayer = AccessTools.Field(typeof(BuildingDef), nameof(BuildingDef.ReplacementLayer));
            MethodInfo isValidBuildLocation = AccessTools.Method(typeof(BuildingDef), nameof(BuildingDef.IsValidBuildLocation),
                new[] { typeof(UnityEngine.GameObject), typeof(UnityEngine.Vector3), typeof(Orientation), typeof(bool) });
            MethodInfo replacement = AccessTools.Method(typeof(ReplaceRules), nameof(ReplaceRules.IsValidReplaceLocation));

            bool replacementLayerSeen = false;
            bool patched = false;
            foreach (CodeInstruction instruction in instructions)
            {
                if (!patched)
                {
                    if (instruction.LoadsField(replacementLayer))
                    {
                        replacementLayerSeen = true;
                    }
                    else if (replacementLayerSeen && instruction.Calls(isValidBuildLocation))
                    {
                        // Même pile : l'instance BuildingDef devient le premier argument de la méthode statique.
                        instruction.opcode = OpCodes.Call;
                        instruction.operand = replacement;
                        patched = true;
                    }
                }
                yield return instruction;
            }

            if (!patched)
                Debug.LogWarning("[SmartConnect] BuildPath: replacement check not found, vanilla behaviour kept");
        }
    }

    /// <summary>
    /// VisUpdater est la coroutine qui redessine l'aperçu du tracé à chaque frame (blanc = valide,
    /// rouge = invalide). Le postfix sur son MoveNext passe juste après et teinte les tuiles existantes.
    /// </summary>
    [HarmonyPatch]
    internal static class BaseUtilityBuildTool_VisUpdater_Patch
    {
        private static FieldInfo thisField;

        internal static bool Prepare()
        {
            return TargetMethod() != null;
        }

        internal static MethodBase TargetMethod()
        {
            MethodInfo visUpdater = AccessTools.Method(typeof(BaseUtilityBuildTool), "VisUpdater");
            return visUpdater != null ? AccessTools.EnumeratorMoveNext(visUpdater) : null;
        }

        internal static void Postfix(object __instance)
        {
            if (!SmartConnectMod.Settings.ShowPreview)
                return;
            if (thisField == null)
                thisField = AccessTools.Field(__instance.GetType(), "<>4__this");
            if (thisField?.GetValue(__instance) is BaseUtilityBuildTool tool)
                ReplaceRules.ApplyPreview(tool);
        }
    }
}
