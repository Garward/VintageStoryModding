using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace RecipeExplorer
{
    internal static class HandbookRecipeAssets
    {
        internal sealed class TextureAsset
        {
            private readonly AssetLocation path;
            private readonly string name;
            private BitmapRef bitmap;
            private LoadedTexture texture;

            public TextureAsset(string name)
            {
                this.name = name;
                path = new AssetLocation("betterhandbook", "textures/" + name + ".png");
                All.Add(this);
            }

            public LoadedTexture Texture
            {
                get
                {
                    if (texture == null)
                    {
                        texture = new LoadedTexture(api);
                    }

                    if (texture.TextureId == 0 && bitmap != null)
                    {
                        api.Render.LoadTexture(bitmap, ref texture);
                    }

                    return texture;
                }
            }

            public int TextureId => Texture?.TextureId ?? 0;

            public void Load()
            {
                try
                {
                    IAsset asset = api.Assets.TryGet(path, true);
                    bitmap = asset?.ToBitmap(api);
                }
                catch
                {
                    bitmap = null;
                    api.Logger.Warning("[BetterHandbook] Optional recipe texture '{0}' could not be loaded; related overlay icon will be skipped.", name);
                }
            }
        }

        private static readonly List<TextureAsset> All = new List<TextureAsset>();
        private static ICoreClientAPI api;

        public static readonly TextureAsset Wrench = new TextureAsset("wrench");
        public static readonly TextureAsset RedOverlay = new TextureAsset("red-overlay");

        public static void Load(ICoreClientAPI clientApi)
        {
            api = clientApi;
            for (int i = 0; i < All.Count; i++)
            {
                All[i].Load();
            }
        }
    }
}
