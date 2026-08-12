using System;
using System.Collections.Generic;
using FairyGUI;

namespace MonoShare
{
    internal sealed class FairyGuiUiManager : IDisposable
    {
        private readonly List<GComponent> _overlayStack = new List<GComponent>();

        public GComponent HudLayer { get; }
        public GComponent OverlayLayer { get; }
        public GComponent PopupLayer { get; }

        public FairyGuiUiManager()
        {
            HudLayer = CreateLayer("FairyHudLayer");
            OverlayLayer = CreateLayer("FairyOverlayLayer");
            PopupLayer = CreateLayer("FairyPopupLayer");

            GRoot.inst.AddChild(HudLayer);
            GRoot.inst.AddChild(OverlayLayer);
            GRoot.inst.AddChild(PopupLayer);

            HudLayer.AddRelation(GRoot.inst, RelationType.Size);
            OverlayLayer.AddRelation(GRoot.inst, RelationType.Size);
            PopupLayer.AddRelation(GRoot.inst, RelationType.Size);
        }

        public bool TryShowOverlay(string packageName, string componentName, out GComponent overlay, out string error)
        {
            overlay = null;
            error = null;

            try
            {
                GObject created = UIPackage.CreateObject(packageName, componentName);
                if (created is not GComponent component)
                {
                    created?.Dispose();
                    error = $"未找到或无法创建 FairyGUI 组件：{packageName}/{componentName}";
                    return false;
                }

                OverlayLayer.AddChild(component);
                component.AddRelation(OverlayLayer, RelationType.Size);

                _overlayStack.Add(component);
                overlay = component;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public void CloseTopOverlay()
        {
            if (_overlayStack.Count == 0)
                return;

            int index = _overlayStack.Count - 1;
            GComponent top = _overlayStack[index];
            _overlayStack.RemoveAt(index);

            try
            {
                top?.Dispose();
            }
            catch
            {
            }
        }

        public void CloseAllOverlays()
        {
            while (_overlayStack.Count > 0)
                CloseTopOverlay();
        }

        public void Dispose()
        {
            try
            {
                CloseAllOverlays();
            }
            catch
            {
            }

            try
            {
                HudLayer?.Dispose();
            }
            catch
            {
            }

            try
            {
                OverlayLayer?.Dispose();
            }
            catch
            {
            }

            try
            {
                PopupLayer?.Dispose();
            }
            catch
            {
            }
        }

        private static GComponent CreateLayer(string name)
        {
            return new GComponent
            {
                name = name,
                opaque = false,
            };
        }
    }
}
