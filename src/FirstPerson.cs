using System;
using GorillaNetworking;
using UnityEngine;

namespace GorillaCaster
{
    /// <summary>
    /// First-person head-cosmetic hiding, the way Pokruk's mod does it: find the worn Face/Hat
    /// cosmetic items and toggle their renderers off locally, so your own hat/glasses don't block
    /// the first-person broadcast. Restores them when you leave first-person.
    /// </summary>
    internal static class FirstPerson
    {
        private static bool _hidden;
        public static bool Hidden => _hidden;

        public static void SetHeadCosmetics(bool show)
        {
            try
            {
                var cc = CosmeticsController.instance;
                var rig = GorillaTagger.Instance != null ? GorillaTagger.Instance.offlineVRRig : null;
                if (cc == null || rig == null || rig.cosmeticsObjectRegistry == null) return;
                var set = cc.currentWornSet;
                if (set.items == null) return;

                foreach (var item in set.items)
                {
                    var cat = item.itemCategory;
                    if (cat == CosmeticsController.CosmeticCategory.Face || cat == CosmeticsController.CosmeticCategory.Hat)
                    {
                        var inst = rig.cosmeticsObjectRegistry.Cosmetic(item.displayName);
                        if (inst != null) inst.ToggleRenderers(show);
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[GorillaCaster] FP cosmetics: " + e.Message); }
        }

        public static void Hide() { if (!_hidden) { _hidden = true; SetHeadCosmetics(false); } }
        public static void Restore() { if (_hidden) { _hidden = false; SetHeadCosmetics(true); } }
    }
}
