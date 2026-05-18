using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace HandbookCache
{
    internal static class HandbookBookmarks
    {
        public const string CategoryCode = "betterhandbook:bookmarks";
        private const string ConfigFile = "betterhandbook-bookmarks.json";

        private static readonly object Sync = new object();
        private static readonly HashSet<string> PageCodes = new HashSet<string>(StringComparer.Ordinal);
        private static bool loaded;
        private static ICoreClientAPI capi;
        private static string lockedPageCode;
        private static int version;

        public static int Version
        {
            get
            {
                lock (Sync)
                {
                    return version;
                }
            }
        }

        public static void EnsureLoaded(ICoreClientAPI api)
        {
            if (api == null) return;

            lock (Sync)
            {
                if (loaded) return;

                capi = api;
                BookmarkStore store = api.LoadModConfig<BookmarkStore>(ConfigFile) ?? new BookmarkStore();
                PageCodes.Clear();
                if (store.PageCodes != null)
                {
                    for (int i = 0; i < store.PageCodes.Count; i++)
                    {
                        string pageCode = store.PageCodes[i];
                        if (!string.IsNullOrWhiteSpace(pageCode))
                        {
                            PageCodes.Add(pageCode);
                        }
                    }
                }
                lockedPageCode = string.IsNullOrWhiteSpace(store.LockedPageCode) ? null : store.LockedPageCode;

                loaded = true;
            }
        }

        public static bool IsBookmarksCategory(string categoryCode)
        {
            return string.Equals(categoryCode, CategoryCode, StringComparison.Ordinal);
        }

        public static bool IsBookmarked(GuiHandbookPage page)
        {
            string pageCode = page?.PageCode;
            if (string.IsNullOrEmpty(pageCode)) return false;

            lock (Sync)
            {
                return PageCodes.Contains(pageCode);
            }
        }

        public static bool IsLocked(GuiHandbookPage page)
        {
            string pageCode = page?.PageCode;
            if (string.IsNullOrEmpty(pageCode)) return false;

            lock (Sync)
            {
                return string.Equals(lockedPageCode, pageCode, StringComparison.Ordinal);
            }
        }

        public static string LockedPageCode(ICoreClientAPI api)
        {
            EnsureLoaded(api);
            lock (Sync)
            {
                return lockedPageCode;
            }
        }

        public static bool Toggle(GuiHandbookPage page, ICoreClientAPI api)
        {
            EnsureLoaded(api);
            string pageCode = page?.PageCode;
            if (string.IsNullOrEmpty(pageCode)) return false;

            bool isBookmarked;
            lock (Sync)
            {
                if (PageCodes.Contains(pageCode))
                {
                    PageCodes.Remove(pageCode);
                    isBookmarked = false;
                }
                else
                {
                    PageCodes.Add(pageCode);
                    isBookmarked = true;
                }

                version++;
                SaveLocked();
            }

            return isBookmarked;
        }

        public static bool ToggleLock(GuiHandbookPage page, ICoreClientAPI api)
        {
            EnsureLoaded(api);
            string pageCode = page?.PageCode;
            if (string.IsNullOrEmpty(pageCode)) return false;

            bool isLocked;
            lock (Sync)
            {
                if (string.Equals(lockedPageCode, pageCode, StringComparison.Ordinal))
                {
                    lockedPageCode = null;
                    isLocked = false;
                }
                else
                {
                    lockedPageCode = pageCode;
                    isLocked = true;
                }

                SaveLocked();
            }

            return isLocked;
        }

        public static GuiTab AppendBookmarkTab(ICoreClientAPI api, List<GuiTab> tabs, string currentCategoryCode, ref int curTab)
        {
            EnsureLoaded(api);
            int tabIndex = tabs.Count;
            GuiTab tab = new HandbookTab
            {
                DataInt = tabIndex,
                Name = "Bookmarks",
                CategoryCode = CategoryCode
            };
            tabs.Add(tab);

            if (IsBookmarksCategory(currentCategoryCode))
            {
                curTab = tabIndex;
            }

            return tab;
        }

        private static void SaveLocked()
        {
            if (capi == null) return;

            BookmarkStore store = new BookmarkStore
            {
                PageCodes = PageCodes.OrderBy(code => code, StringComparer.Ordinal).ToList(),
                LockedPageCode = lockedPageCode
            };

            capi.StoreModConfig(store, ConfigFile);
        }

        private sealed class BookmarkStore
        {
            public List<string> PageCodes { get; set; } = new List<string>();
            public string LockedPageCode { get; set; }
        }
    }

    [HarmonyPatch(typeof(GuiDialogHandbook), "initDetailGui")]
    internal static class HandbookBookmarkButtonPatch
    {
        private const string ButtonKey = "betterhandbook-bookmark-button";
        private const string LockButtonKey = "betterhandbook-lock-button";

        private static readonly AccessTools.FieldRef<GuiDialogHandbook, Stack<BrowseHistoryElement>> BrowseHistory =
            AccessTools.FieldRefAccess<GuiDialogHandbook, Stack<BrowseHistoryElement>>("browseHistory");

        private static readonly AccessTools.FieldRef<GuiDialogHandbook, GuiComposer> DetailViewGui =
            AccessTools.FieldRefAccess<GuiDialogHandbook, GuiComposer>("detailViewGui");

        private static readonly AccessTools.FieldRef<GuiDialog, ICoreClientAPI> ClientApi =
            AccessTools.FieldRefAccess<GuiDialog, ICoreClientAPI>("capi");

        public static void Postfix(GuiDialogHandbook __instance)
        {
            ICoreClientAPI api = ClientApi(__instance);

            try
            {
                Stack<BrowseHistoryElement> browseHistory = BrowseHistory(__instance);
                if (browseHistory == null || browseHistory.Count == 0) return;

                GuiHandbookPage page = browseHistory.Peek().Page;
                if (page == null) return;

                GuiComposer detailViewGui = DetailViewGui(__instance);
                if (detailViewGui == null || detailViewGui.GetElement(ButtonKey) != null || detailViewGui.GetElement(LockButtonKey) != null) return;

                HandbookBookmarks.EnsureLoaded(api);
                AddBookmarkButtons(detailViewGui, page, api);
                detailViewGui.ReCompose();
            }
            catch (Exception ex)
            {
                HandbookCacheDiagnostics.LogFailure(api, "Failed to add bookmark button: {0}", ex);
            }
        }

        private static void AddBookmarkButtons(GuiComposer detailViewGui, GuiHandbookPage page, ICoreClientAPI api)
        {
            bool isBookmarked = HandbookBookmarks.IsBookmarked(page);
            bool isLocked = HandbookBookmarks.IsLocked(page);
            HeaderIconButton bookmarkButton = new HeaderIconButton(
                api,
                CreateButtonBounds(),
                HeaderIconKind.Bookmark,
                isBookmarked,
                () => HandbookBookmarks.Toggle(page, api));
            HeaderIconButton lockButton = new HeaderIconButton(
                api,
                CreateButtonBounds(),
                HeaderIconKind.Lock,
                isLocked,
                () => HandbookBookmarks.ToggleLock(page, api));

            PositionAfterTitle(detailViewGui, bookmarkButton.Bounds, 0);
            PositionAfterTitle(detailViewGui, lockButton.Bounds, 1);
            detailViewGui.AddInteractiveElement(bookmarkButton, ButtonKey);
            detailViewGui.AddInteractiveElement(lockButton, LockButtonKey);
        }

        private static ElementBounds CreateButtonBounds()
        {
            return ElementBounds
                .Fixed(0, 0, 24, 22)
                .WithAlignment(EnumDialogArea.None);
        }

        private static void PositionAfterTitle(GuiComposer detailViewGui, ElementBounds buttonBounds, int slotFromTitle)
        {
            double scale = RuntimeEnv.GUIScale;
            ElementBounds parent = detailViewGui.Bounds;
            double absX = parent.absX + (177 + slotFromTitle * 28) * scale;
            double absY = parent.absY + 6 * scale;

            buttonBounds.Alignment = EnumDialogArea.None;
            buttonBounds.fixedOffsetX = 0;
            buttonBounds.fixedOffsetY = 0;
            buttonBounds.fixedX = (absX - parent.absX - parent.absPaddingX) / scale;
            buttonBounds.fixedY = (absY - parent.absY - parent.absPaddingY) / scale;
        }

        private enum HeaderIconKind
        {
            Bookmark,
            Lock
        }

        private sealed class HeaderIconButton : GuiElement
        {
            private readonly HeaderIconKind iconKind;
            private readonly Func<bool> onClick;
            private int normalTextureId;
            private int hoverTextureId;
            private int pressedTextureId;
            private bool active;
            private bool isOver;
            private bool currentlyMouseDownOnElement;

            public HeaderIconButton(ICoreClientAPI api, ElementBounds bounds, HeaderIconKind iconKind, bool active, Func<bool> onClick)
                : base(api, bounds)
            {
                this.iconKind = iconKind;
                this.active = active;
                this.onClick = onClick;
                MouseOverCursor = "pointer";
            }

            public override bool Focusable => true;

            public override void ComposeElements(Context ctxStatic, ImageSurface surfaceStatic)
            {
                Bounds.CalcWorldBounds();
                Redraw();
            }

            public override void RenderInteractiveElements(float deltaTime)
            {
                int textureId = currentlyMouseDownOnElement ? pressedTextureId : isOver ? hoverTextureId : normalTextureId;
                api.Render.Render2DTexturePremultipliedAlpha(textureId, Bounds);
            }

            public override void OnMouseMove(ICoreClientAPI api, MouseEvent args)
            {
                bool wasOver = isOver;
                isOver = Bounds.PointInside(api.Input.MouseX, api.Input.MouseY);
                if (!wasOver && isOver)
                {
                    api.Gui.PlaySound("menubutton");
                }
            }

            public override void OnMouseDownOnElement(ICoreClientAPI api, MouseEvent args)
            {
                if (args.Button != EnumMouseButton.Left && args.Button != EnumMouseButton.Right) return;

                base.OnMouseDownOnElement(api, args);
                currentlyMouseDownOnElement = true;
                api.Gui.PlaySound("menubutton_down");
            }

            public override void OnMouseUp(ICoreClientAPI api, MouseEvent args)
            {
                if (currentlyMouseDownOnElement && !Bounds.PointInside(args.X, args.Y))
                {
                    api.Gui.PlaySound("menubutton_up");
                }

                base.OnMouseUp(api, args);
                currentlyMouseDownOnElement = false;
            }

            public override void OnMouseUpOnElement(ICoreClientAPI api, MouseEvent args)
            {
                if (currentlyMouseDownOnElement && Bounds.PointInside(args.X, args.Y) && (args.Button == EnumMouseButton.Left || args.Button == EnumMouseButton.Right))
                {
                    active = onClick();
                    api.Gui.PlaySound("menubutton_press");
                    Redraw();
                    args.Handled = true;
                }

                currentlyMouseDownOnElement = false;
            }

            public override void Dispose()
            {
                base.Dispose();
                DeleteTexture(ref normalTextureId);
                DeleteTexture(ref hoverTextureId);
                DeleteTexture(ref pressedTextureId);
            }

            private void Redraw()
            {
                GenerateButtonTexture(ref normalTextureId, hover: false, pressed: false);
                GenerateButtonTexture(ref hoverTextureId, hover: true, pressed: false);
                GenerateButtonTexture(ref pressedTextureId, hover: false, pressed: true);
            }

            private void GenerateButtonTexture(ref int textureId, bool hover, bool pressed)
            {
                using (ImageSurface surface = new ImageSurface((Format)0, Bounds.OuterWidthInt, Bounds.OuterHeightInt))
                {
                    using (Context ctx = genContext(surface))
                    {
                        DrawButton(ctx, hover, pressed);
                        generateTexture(surface, ref textureId);
                    }
                }
            }

            private void DrawButton(Context ctx, bool hover, bool pressed)
            {
                double width = Bounds.OuterWidth;
                double height = Bounds.OuterHeight;
                double embossHeight = scaled(1.5);

                Rectangle(ctx, 0, 0, width, height);
                ctx.SetSourceRGBA(GuiStyle.ButtonBackColor);
                ctx.Fill();

                Rectangle(ctx, 0, 0, width - embossHeight, embossHeight);
                ctx.SetSourceRGBA(1, 1, 1, pressed ? 0.05 : 0.15);
                ctx.Fill();
                Rectangle(ctx, 0, embossHeight, embossHeight, height - embossHeight);
                ctx.Fill();

                Rectangle(ctx, embossHeight, height - embossHeight, width - 2 * embossHeight, embossHeight);
                ctx.SetSourceRGBA(0, 0, 0, pressed ? 0.35 : 0.2);
                ctx.Fill();
                Rectangle(ctx, width - embossHeight, 0, embossHeight, height);
                ctx.Fill();

                if (hover && !pressed)
                {
                    ctx.SetSourceRGBA(1, 1, 1, 0.11);
                    Rectangle(ctx, 0, 0, width, height);
                    ctx.Fill();
                }

                if (active)
                {
                    ctx.SetSourceRGBA(1, 0.74, 0.28, 0.12);
                    Rectangle(ctx, 2, 2, width - 4, height - 4);
                    ctx.Fill();
                }

                if (iconKind == HeaderIconKind.Bookmark)
                {
                    DrawBookmarkIcon(ctx, width, height);
                }
                else
                {
                    DrawLockIcon(ctx, width, height);
                }
            }

            private void DrawBookmarkIcon(Context ctx, double width, double height)
            {
                double x = width / 2 - 5;
                double y = 4;
                double w = 10;
                double h = 14;
                SetIconColor(ctx);
                ctx.LineWidth = active ? scaled(1.4) : scaled(1.7);
                ctx.MoveTo(x, y);
                ctx.LineTo(x + w, y);
                ctx.LineTo(x + w, y + h);
                ctx.LineTo(x + w / 2, y + h - 4);
                ctx.LineTo(x, y + h);
                ctx.ClosePath();

                if (active)
                {
                    ctx.FillPreserve();
                    ctx.SetSourceRGBA(0.12, 0.09, 0.04, 0.38);
                }

                ctx.Stroke();
            }

            private void DrawLockIcon(Context ctx, double width, double height)
            {
                double bodyX = width / 2 - 5.5;
                double bodyY = 10;
                double bodyW = 11;
                double bodyH = 7;
                SetIconColor(ctx);
                ctx.LineWidth = scaled(1.55);

                if (active)
                {
                    ctx.Arc(width / 2, bodyY, 4.2, Math.PI, 0);
                }
                else
                {
                    ctx.Arc(width / 2 + 1.2, bodyY, 4.2, Math.PI * 1.06, Math.PI * 1.88);
                }

                ctx.Stroke();

                Rectangle(ctx, bodyX, bodyY, bodyW, bodyH);
                if (active)
                {
                    ctx.FillPreserve();
                    ctx.SetSourceRGBA(0.12, 0.09, 0.04, 0.38);
                }

                ctx.Stroke();

                ctx.Arc(width / 2, bodyY + 3.5, 0.9, 0, Math.PI * 2);
                ctx.Fill();
            }

            private void SetIconColor(Context ctx)
            {
                if (active)
                {
                    ctx.SetSourceRGBA(1, 0.76, 0.3, 1);
                }
                else
                {
                    ctx.SetSourceRGBA(0.86, 0.79, 0.64, 0.95);
                }
            }

            private void DeleteTexture(ref int textureId)
            {
                if (textureId > 0)
                {
                    api.Render.GLDeleteTexture(textureId);
                    textureId = 0;
                }
            }
        }
    }
}
