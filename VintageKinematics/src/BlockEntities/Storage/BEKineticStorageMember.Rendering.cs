using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Storage.Rendering;

namespace VintageKinematics.BlockEntities.Storage
{
    public abstract partial class BEKineticStorageMember
    {
        private string visualConnectionMask = string.Empty;
        private IReadOnlyList<string> visualConcaveElbows = Array.Empty<string>();
        private string visualConnectionSignature = string.Empty;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side != EnumAppSide.Client) return;
            RefreshVisualConnections();
            // Neighbor notifications handle live edits. Two bounded retries cover arbitrary
            // block-entity initialization order after a chunk/relog without leaving one
            // permanent polling listener on every storage cell.
            RegisterDelayedCallback(_ => RefreshVisualConnections(), 100);
            RegisterDelayedCallback(_ => RefreshVisualConnections(), 1000);
        }

        public override bool OnTesselation(
            ITerrainMeshPool mesher,
            ITesselatorAPI tessThreadTesselator)
        {
            if (Api is not ICoreClientAPI capi || Block == null) return false;

            StorageConnectedShapeSelection selection = this is BEKineticWarehousePort
                ? StorageConnectedShapeSelector.SelectPort(
                    Block.Variant?["port"],
                    Block.Variant?["side"])
                : IsController
                    ? StorageConnectedShapeSelector.SelectController(Block.Variant?["side"])
                    : StorageConnectedShapeSelector.SelectCell(visualConnectionMask);
            if (!AddShape(selection, capi, mesher, tessThreadTesselator)) return false;
            if (!IsController && this is not BEKineticWarehousePort)
            {
                foreach (string elbow in visualConcaveElbows)
                {
                    StorageConnectedShapeSelection overlay =
                        StorageConnectedShapeSelector.SelectElbow(elbow);
                    AddShape(overlay, capi, mesher, tessThreadTesselator);
                }
            }
            return true;
        }

        private bool AddShape(
            StorageConnectedShapeSelection selection,
            ICoreClientAPI capi,
            ITerrainMeshPool mesher,
            ITesselatorAPI tessellator)
        {
            if (selection == null) return false;
            Shape shape = Shape.TryGet(capi, new AssetLocation(selection.ShapePath));
            if (shape == null) return false;
            tessellator.TesselateShape(
                Block,
                shape,
                out MeshData mesh,
                new Vec3f(selection.RotateX, selection.RotateY, selection.RotateZ));
            if (mesh != null) mesher.AddMeshData(mesh);
            return true;
        }

        internal void RefreshVisualConnections()
        {
            if (Api?.Side != EnumAppSide.Client || Pos == null) return;
            string refreshed = StorageVisualConnectionScanner.Scan(Api.World, Pos)
                ?? string.Empty;
            IReadOnlyList<string> elbows = IsController || this is BEKineticWarehousePort
                ? Array.Empty<string>()
                : StorageVisualConnectionScanner.ScanConcaveElbows(Api.World, Pos, refreshed);
            string signature = refreshed + "|" + string.Join(",", elbows);
            if (string.Equals(visualConnectionSignature, signature, StringComparison.Ordinal)) return;

            visualConnectionMask = refreshed;
            visualConcaveElbows = elbows;
            visualConnectionSignature = signature;
            Api.World.BlockAccessor.MarkBlockDirty(Pos);
        }
    }
}
