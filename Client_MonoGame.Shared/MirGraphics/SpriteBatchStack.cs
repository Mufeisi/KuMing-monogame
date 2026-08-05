using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace MonoShare.MirGraphics
{
    /// <summary>
    /// SpriteBatch 的 Begin/End 栈管理器：
    /// - 允许在同一帧内通过嵌套 Begin/End 共享一次真正的 Begin/End；
    /// - 当需要切换不同 BlendState 时，会自动 End/Begin 并在退出作用域后恢复上层状态。
    /// </summary>
    public sealed class SpriteBatchStack
    {
        private readonly SpriteBatch _spriteBatch;
        private readonly Stack<SpriteBatchSettings> _settingsStack = new Stack<SpriteBatchSettings>();

        private SpriteBatchSettings _activeSettings;
        private bool _begun;

        public SpriteBatchStack(SpriteBatch spriteBatch)
        {
            _spriteBatch = spriteBatch ?? throw new ArgumentNullException(nameof(spriteBatch));
        }

        public int Depth => _settingsStack.Count;

        public int FrameBeginCalls { get; private set; }
        public int FrameEndCalls { get; private set; }
        public int FrameStateChanges { get; private set; }

        public void ResetFrameMetrics()
        {
            FrameBeginCalls = 0;
            FrameEndCalls = 0;
            FrameStateChanges = 0;
        }

        public void Begin()
        {
            Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend);
        }

        public void Begin(
            SpriteSortMode sortMode,
            BlendState blendState,
            SamplerState samplerState = null,
            DepthStencilState depthStencilState = null,
            RasterizerState rasterizerState = null,
            Effect effect = null,
            Matrix? transformMatrix = null)
        {
            blendState ??= BlendState.AlphaBlend;

            SpriteBatchSettings inherited = _settingsStack.Count > 0 ? _settingsStack.Peek() : default;

            _settingsStack.Push(new SpriteBatchSettings(
                sortMode,
                blendState,
                samplerState ?? inherited.SamplerState,
                depthStencilState ?? inherited.DepthStencilState,
                rasterizerState ?? inherited.RasterizerState,
                effect ?? inherited.Effect,
                transformMatrix ?? inherited.TransformMatrix));
            ApplyTopState();
        }

        public void End()
        {
            if (_settingsStack.Count == 0)
                return;

            _settingsStack.Pop();

            if (_settingsStack.Count == 0)
            {
                if (_begun)
                {
                    EndSpriteBatch();
                    _begun = false;
                }
                return;
            }

            ApplyTopState();
        }

        public void Reset()
        {
            _settingsStack.Clear();

            if (!_begun)
                return;

            EndSpriteBatch();
            _begun = false;
        }

        private void ApplyTopState()
        {
            SpriteBatchSettings desired = _settingsStack.Peek();

            if (!_begun)
            {
                BeginSpriteBatch(desired);
                _activeSettings = desired;
                _begun = true;
                return;
            }

            if (_activeSettings.Equals(desired))
                return;

            EndSpriteBatch();
            FrameStateChanges++;
            BeginSpriteBatch(desired);
            _activeSettings = desired;
        }

        private void BeginSpriteBatch(SpriteBatchSettings desired)
        {
            bool hasExtendedState =
                desired.SamplerState != null ||
                desired.DepthStencilState != null ||
                desired.RasterizerState != null ||
                desired.Effect != null ||
                desired.TransformMatrix != null;

            if (!hasExtendedState)
            {
                _spriteBatch.Begin(desired.SortMode, desired.BlendState);
                FrameBeginCalls++;
                return;
            }

            _spriteBatch.Begin(
                desired.SortMode,
                desired.BlendState,
                desired.SamplerState,
                desired.DepthStencilState,
                desired.RasterizerState,
                desired.Effect,
                desired.TransformMatrix);
            FrameBeginCalls++;
        }

        private void EndSpriteBatch()
        {
            _spriteBatch.End();
            FrameEndCalls++;
        }

        private readonly struct SpriteBatchSettings : IEquatable<SpriteBatchSettings>
        {
            public SpriteBatchSettings(
                SpriteSortMode sortMode,
                BlendState blendState,
                SamplerState samplerState,
                DepthStencilState depthStencilState,
                RasterizerState rasterizerState,
                Effect effect,
                Matrix? transformMatrix)
            {
                SortMode = sortMode;
                BlendState = blendState;
                SamplerState = samplerState;
                DepthStencilState = depthStencilState;
                RasterizerState = rasterizerState;
                Effect = effect;
                TransformMatrix = transformMatrix;
            }

            public SpriteSortMode SortMode { get; }
            public BlendState BlendState { get; }
            public SamplerState SamplerState { get; }
            public DepthStencilState DepthStencilState { get; }
            public RasterizerState RasterizerState { get; }
            public Effect Effect { get; }
            public Matrix? TransformMatrix { get; }

            private static Matrix NormalizeTransform(Matrix? transformMatrix)
            {
                return transformMatrix ?? Matrix.Identity;
            }

            public bool Equals(SpriteBatchSettings other)
            {
                return SortMode == other.SortMode &&
                       ReferenceEquals(BlendState, other.BlendState) &&
                       ReferenceEquals(SamplerState, other.SamplerState) &&
                       ReferenceEquals(DepthStencilState, other.DepthStencilState) &&
                       ReferenceEquals(RasterizerState, other.RasterizerState) &&
                       ReferenceEquals(Effect, other.Effect) &&
                       NormalizeTransform(TransformMatrix).Equals(NormalizeTransform(other.TransformMatrix));
            }

            public override bool Equals(object obj) => obj is SpriteBatchSettings other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (int)SortMode;
                    hash = (hash * 397) ^ (BlendState != null ? BlendState.GetHashCode() : 0);
                    hash = (hash * 397) ^ (SamplerState != null ? SamplerState.GetHashCode() : 0);
                    hash = (hash * 397) ^ (DepthStencilState != null ? DepthStencilState.GetHashCode() : 0);
                    hash = (hash * 397) ^ (RasterizerState != null ? RasterizerState.GetHashCode() : 0);
                    hash = (hash * 397) ^ (Effect != null ? Effect.GetHashCode() : 0);
                    hash = (hash * 397) ^ NormalizeTransform(TransformMatrix).GetHashCode();
                    return hash;
                }
            }
        }
    }
}
