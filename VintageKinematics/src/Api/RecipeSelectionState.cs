using System;
using System.IO;
using Vintagestory.API.Datastructures;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Shared persisted selection state for machine recipe browsers that need a clicked recipe
    /// to constrain processing instead of only browsing recipes.
    /// </summary>
    public class RecipeSelectionState<TRecipe> where TRecipe : class
    {
        private readonly Func<string, TRecipe> findByKey;
        private string selectedKey = "";

        public RecipeSelectionState(Func<string, TRecipe> findByKey)
        {
            this.findByKey = findByKey;
        }

        public string SelectedKey => selectedKey ?? "";

        public bool Set(string key)
        {
            key ??= "";
            if (key.Length > 0 && findByKey?.Invoke(key) == null) return false;
            if (selectedKey == key) return false;

            selectedKey = key;
            return true;
        }

        public TRecipe GetSelected()
        {
            return string.IsNullOrEmpty(selectedKey) ? null : findByKey?.Invoke(selectedKey);
        }

        public TRecipe GetSelectedIf(Func<TRecipe, bool> predicate)
        {
            TRecipe recipe = GetSelected();
            if (recipe == null) return null;
            return predicate == null || predicate(recipe) ? recipe : null;
        }

        public void WriteToTree(ITreeAttribute tree, string attributeName = "selectedRecipeKey")
        {
            tree?.SetString(attributeName, SelectedKey);
        }

        public void ReadFromTree(ITreeAttribute tree, string attributeName = "selectedRecipeKey")
        {
            selectedKey = tree?.GetString(attributeName, "") ?? "";
        }

        public static byte[] ToPacket(string key)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(key ?? "");
            return ms.ToArray();
        }

        public static string FromPacket(byte[] data)
        {
            try
            {
                using var ms = new MemoryStream(data ?? Array.Empty<byte>());
                using var br = new BinaryReader(ms);
                return br.ReadString() ?? "";
            }
            catch
            {
                return "";
            }
        }
    }
}
