using System.Collections.Generic;
using UnityEngine;

namespace EasyBuilder
{
    /// <summary>
    /// Keeps the batch's profile assets loaded while it runs. A player build unloads every asset that only
    /// scripts reference, which destroyed the profile objects halfway through a batch; assets referenced
    /// from this hidden object count as in use.
    /// </summary>
    internal sealed class BatchAssetHolder : ScriptableObject
    {
        [SerializeField] private List<Object> assets = new();

        public static BatchAssetHolder Create(IEnumerable<Object> assetsToKeep)
        {
            var holder = CreateInstance<BatchAssetHolder>();
            holder.hideFlags = HideFlags.HideAndDontSave;

            foreach (var asset in assetsToKeep)
                if (asset != null)
                    holder.assets.Add(asset);

            return holder;
        }
    }
}
