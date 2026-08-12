using FontStashSharp.Interfaces;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace FairyGUI
{
	internal sealed class FontStashSharpVertexBufferRenderer : IFontStashRenderer2
	{
		private readonly VertexBuffer _vertexBuffer;
		private Texture2D _firstTexture;

		public FontStashSharpVertexBufferRenderer(GraphicsDevice graphicsDevice, VertexBuffer vertexBuffer)
		{
			GraphicsDevice = graphicsDevice;
			_vertexBuffer = vertexBuffer;
		}

		public GraphicsDevice GraphicsDevice { get; }

		public Texture2D Texture => _firstTexture;

		public bool HasMultipleTextures { get; private set; }

		private static Vector2 FixUv(Vector2 uv)
		{
#if ANDROID || IOS
			// FairyGUI(MonoGame/OpenGL) 使用的贴图坐标系与 SpriteBatch/FontStashSharp 默认约定不同（V 轴方向相反）。
			// 不做转换会导致文本采样到错误区域，表现为“文字完全不显示”。
			return new Vector2(uv.X, 1f - uv.Y);
#else
			return uv;
#endif
		}

		public void DrawQuad(Texture2D texture, ref VertexPositionColorTexture topLeft, ref VertexPositionColorTexture topRight,
			ref VertexPositionColorTexture bottomLeft, ref VertexPositionColorTexture bottomRight)
		{
			if (_firstTexture == null)
				_firstTexture = texture;
			else if (!ReferenceEquals(_firstTexture, texture))
				HasMultipleTextures = true;

			int start = _vertexBuffer.currentVertCount;

			// FairyGUI 的 quad 顶点顺序约定为：
			// 1---2
			// | / |
			// 0---3
			// 这里将 FontStashSharp 的顶点（topLeft/topRight/bottomLeft/bottomRight）映射到上述顺序。
			_vertexBuffer.AddVert(new Vector3(bottomLeft.Position.X, bottomLeft.Position.Y, 0f), bottomLeft.Color, FixUv(bottomLeft.TextureCoordinate));
			_vertexBuffer.AddVert(new Vector3(topLeft.Position.X, topLeft.Position.Y, 0f), topLeft.Color, FixUv(topLeft.TextureCoordinate));
			_vertexBuffer.AddVert(new Vector3(topRight.Position.X, topRight.Position.Y, 0f), topRight.Color, FixUv(topRight.TextureCoordinate));
			_vertexBuffer.AddVert(new Vector3(bottomRight.Position.X, bottomRight.Position.Y, 0f), bottomRight.Color, FixUv(bottomRight.TextureCoordinate));

			_vertexBuffer.AddTriangle(start, start + 1, start + 2);
			_vertexBuffer.AddTriangle(start + 2, start + 3, start);
		}
	}
}
