using System;
using System.Collections.Generic;
using FairyGUI.Utils;
using FontStashSharp;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoShare;
using Rectangle = System.Drawing.RectangleF;

namespace FairyGUI
{
	/// <summary>
	/// 跨平台文本控件（精简版）。
	///
	/// 目标：
	/// - 先让 FairyGUI Runtime 能在本仓库各目标（Windows/Android）编译通过；
	/// - 保留 TextField 对外 API（供 GTextField / InputTextField 等调用）；
	/// - 后续再逐步补齐真实渲染（FontStashSharp）与富文本/描边/阴影等效果。
	/// </summary>
	public class TextField : DisplayObject, IMeshFactory
	{
		private const int GUTTER_X = 2;
		private const int GUTTER_Y = 2;

		private static readonly Dictionary<Texture2D, NTexture> FontTextureCache = new Dictionary<Texture2D, NTexture>();

		private static NTexture GetOrCreateFontTexture(Texture2D texture)
		{
			if (texture == null)
				return NTexture.Empty;

			if (!FontTextureCache.TryGetValue(texture, out NTexture ntex))
			{
				ntex = new NTexture(texture);
				FontTextureCache.Add(texture, ntex);
			}

			return ntex;
		}

		private VertAlignType _verticalAlign;
		private TextFormat _textFormat;
		private bool _input;
		private string _text;
		private bool _html;
		private AutoSizeType _autoSize;
		private bool _wordWrap;
		private bool _singleLine;
		private int _maxWidth;

		private int _stroke;
		private Color _strokeColor;
		private Vector2 _shadowOffset;

		private readonly List<HtmlElement> _elements;
		private readonly List<LineInfo> _lines;
		private List<CharPosition> _charPositions;
		private string _renderText;
		private readonly List<RichTextRun> _richRuns;
		private readonly List<LinkSpan> _linkSpans;

		private BaseFont _font;
		private float _textWidth;
		private float _textHeight;
		private float _minHeight;
		private bool _textChanged;
		private int _yOffset;
		private float _fontSizeScale;
		private float _globalScale;
		private RichTextField _richTextField;

		private bool _updatingSize;

		private struct RichTextRun
		{
			public int Start;
			public int End;
			public TextFormat Format;
		}

		private struct LinkSpan
		{
			public HtmlElement Element;
			public int Start;
			public int End;
		}

		public TextField()
		{
			_touchDisabled = true;

			_textFormat = new TextFormat();
			_strokeColor = Color.Black;
			_shadowOffset = Vector2.Zero;

			_wordWrap = false;
			_singleLine = false;
			_text = string.Empty;
			_elements = new List<HtmlElement>(0);
			_lines = new List<LineInfo>(1);
			_renderText = string.Empty;
			_richRuns = new List<RichTextRun>(8);
			_linkSpans = new List<LinkSpan>(4);

			_fontSizeScale = 1f;
			_globalScale = UIContentScaler.scaleFactor;
			_textChanged = true;

			graphics = new NGraphics();
			graphics.pixelSnapping = true;
			graphics.meshFactory = this;
			// TextField 需要一个非空 texture 才会触发 NGraphics.UpdateMesh；实际渲染时会切换到字体图集。
			graphics.texture = NTexture.Empty;
		}

		public override void Dispose()
		{
			base.Dispose();
		}

		internal void EnableRichSupport(RichTextField richTextField)
		{
			_richTextField = richTextField;
			if (_richTextField is InputTextField)
			{
				_input = true;
				_charPositions ??= new List<CharPosition>();
				_textChanged = true;
			}
		}

		public TextFormat textFormat
		{
			get => _textFormat;
			set
			{
				_textFormat = value ?? new TextFormat();
				ResolveFont();
				if (!string.IsNullOrEmpty(_text))
					_textChanged = true;
			}
		}

		public AlignType align
		{
			get => _textFormat.align;
			set
			{
				if (_textFormat.align == value)
					return;

				_textFormat.align = value;
				_textChanged = true;
			}
		}

		public VertAlignType verticalAlign
		{
			get => _verticalAlign;
			set
			{
				if (_verticalAlign == value)
					return;

				_verticalAlign = value;
				ApplyVertAlign();
			}
		}

		public string text
		{
			get => _text;
			set
			{
				_text = ToolSet.FormatCRLF(value ?? string.Empty);
				_textChanged = true;
				_html = false;
			}
		}

		public string htmlText
		{
			get => _text;
			set
			{
				_text = value ?? string.Empty;
				_textChanged = true;
				_html = true;
			}
		}

		public AutoSizeType autoSize
		{
			get => _autoSize;
			set
			{
				if (_autoSize == value)
					return;

				_autoSize = value;
				_textChanged = true;
			}
		}

		public bool wordWrap
		{
			get => _wordWrap;
			set
			{
				if (_wordWrap == value)
					return;

				_wordWrap = value;
				_textChanged = true;
			}
		}

		public bool singleLine
		{
			get => _singleLine;
			set
			{
				if (_singleLine == value)
					return;

				_singleLine = value;
				_textChanged = true;
			}
		}

		public int stroke
		{
			get => _stroke;
			set
			{
				if (_stroke == value)
					return;

				_stroke = value;
				graphics.SetMeshDirty();
			}
		}

		public Color strokeColor
		{
			get => _strokeColor;
			set
			{
				_strokeColor = value;
				graphics.SetMeshDirty();
			}
		}

		public Vector2 shadowOffset
		{
			get => _shadowOffset;
			set
			{
				_shadowOffset = value;
				graphics.SetMeshDirty();
			}
		}

		public int maxWidth
		{
			get => _maxWidth;
			set
			{
				if (_maxWidth == value)
					return;

				_maxWidth = value;
				_textChanged = true;
			}
		}

		public float textWidth
		{
			get
			{
				if (_textChanged)
					BuildLines();
				return _textWidth;
			}
		}

		public float textHeight
		{
			get
			{
				if (_textChanged)
					BuildLines();
				return _textHeight;
			}
		}

		public List<HtmlElement> htmlElements
		{
			get
			{
				if (_textChanged)
					BuildLines();
				return _elements;
			}
		}

		public List<LineInfo> lines
		{
			get
			{
				if (_textChanged)
					BuildLines();
				return _lines;
			}
		}

		public List<CharPosition> charPositions
		{
			get
			{
				if (_textChanged)
					BuildLines();

				graphics.UpdateMesh();
				return _charPositions ??= new List<CharPosition> { new CharPosition { charIndex = 0, lineIndex = 0, offsetX = GUTTER_X } };
			}
		}

		public RichTextField richTextField => _richTextField;

		public bool Rebuild()
		{
			if (_globalScale != UIContentScaler.scaleFactor)
				_textChanged = true;

			if (_textChanged)
				BuildLines();

			return graphics.UpdateMesh();
		}

		public void GetLinesShape(int startLine, float startCharX, int endLine, float endCharX, bool clipped, List<Rectangle> resultRects)
		{
			if (resultRects == null)
				return;

			if (_textChanged)
				BuildLines();

			if (_lines.Count == 0)
				return;

			startLine = Math.Clamp(startLine, 0, _lines.Count - 1);
			endLine = Math.Clamp(endLine, 0, _lines.Count - 1);

			LineInfo line1 = _lines[startLine];
			LineInfo line2 = _lines[endLine];

			if (startLine == endLine)
			{
				Rectangle r = new Rectangle(startCharX, line1.y, endCharX - startCharX, line1.height);
				resultRects.Add(clipped ? ToolSet.Intersection(ref r, ref _contentRect) : r);
				return;
			}

			// 简化：跨行时，以行高切片填充。
			Rectangle head = new Rectangle(startCharX, line1.y, GUTTER_X + line1.width - startCharX, line1.height);
			resultRects.Add(clipped ? ToolSet.Intersection(ref head, ref _contentRect) : head);

			for (int i = startLine + 1; i < endLine; i++)
			{
				LineInfo line = _lines[i];
				Rectangle mid = new Rectangle(GUTTER_X, line.y, line.width, line.height);
				resultRects.Add(clipped ? ToolSet.Intersection(ref mid, ref _contentRect) : mid);
			}

			Rectangle tail = new Rectangle(GUTTER_X, line2.y, endCharX - GUTTER_X, line2.height);
			resultRects.Add(clipped ? ToolSet.Intersection(ref tail, ref _contentRect) : tail);
		}

		public override void EnsureSizeCorrect()
		{
			if (_updatingSize)
				return;

			// RichTextField 会在 Update 前主动调用 Rebuild。
			if (_richTextField == null)
				Rebuild();
		}

		protected override void OnSizeChanged(bool widthChanged, bool heightChanged)
		{
			base.OnSizeChanged(widthChanged, heightChanged);

			if (_updatingSize)
				return;

			_textChanged = true;
		}

		public void OnPopulateMesh(VertexBuffer vb)
		{
			if (_textChanged)
				BuildLines();

			string text = _renderText ?? string.Empty;
			if (string.IsNullOrEmpty(text))
				return;

			// publish 内若指定了 BitmapFont，则优先保证绑定正确的贴图（网格渲染可后续补齐）。
			if (_font is BitmapFont)
			{
				EnsureFontTextureReady();
				return;
			}

			EnsureFontTextureReady();

			FontSystem fontSystem = CMain.fontSystem;
			if (fontSystem == null)
				return;

			int fontSize = _textFormat.size;
			if (_font is DynamicFont dyn && dyn.Size > 0)
				fontSize = dyn.Size;
			fontSize = Math.Max(1, fontSize);

			SpriteFontBase spriteFont = fontSystem.GetFont(fontSize);
			if (spriteFont == null)
				return;

			TextStyle textStyle = TextStyle.None;
			if (_textFormat.underline)
				textStyle |= TextStyle.Underline;

			FontSystemEffect effect = FontSystemEffect.None;
			int effectAmount = 0;
			if (_stroke > 0)
			{
				effect = FontSystemEffect.Stroked;
				effectAmount = _stroke;
			}

			Color textColor = _textFormat.color;
			if (alpha < 1f)
			{
				int a = (int)Math.Round(textColor.A * alpha);
				if (a < 0) a = 0;
				if (a > 255) a = 255;
				textColor = new Color(textColor.R, textColor.G, textColor.B, (byte)a);
			}

			float rectWidth = _contentRect.Width - GUTTER_X * 2;
			if (rectWidth < 0)
				rectWidth = 0;

			FontStashSharpVertexBufferRenderer renderer = new FontStashSharpVertexBufferRenderer(Stage.game.GraphicsDevice, vb);

			bool rich = _html && _richRuns != null && _richRuns.Count > 0;
			int runIndex = 0;

			for (int i = 0; i < _lines.Count; i++)
			{
				LineInfo line = _lines[i];
				int start = line.startCharIndex;
				int end = line.endCharIndex;
				if (end <= start)
					continue;
				if (start < 0 || end > text.Length)
					continue;

				float startX = GUTTER_X;
				if (_textFormat.align == AlignType.Center)
					startX = GUTTER_X + (rectWidth - line.width) / 2f;
				else if (_textFormat.align == AlignType.Right)
					startX = GUTTER_X + (rectWidth - line.width);

				if (startX < GUTTER_X)
					startX = GUTTER_X;

				if (!rich)
				{
					string lineText = text.Substring(start, end - start);
					if (lineText.Length == 0)
						continue;

					Vector2 pos = new Vector2(startX, line.y);
					spriteFont.DrawText(renderer, lineText, pos, textColor, 0f, Vector2.Zero, null, 0f,
						_textFormat.letterSpacing, 0f, textStyle, effect, effectAmount);
					continue;
				}

				// Rich: 按运行段分别绘制（颜色/下划线）
				float xPos = startX;

				while (runIndex < _richRuns.Count && _richRuns[runIndex].End <= start)
					runIndex++;

				int localRun = runIndex;
				int posIndex = start;

				while (posIndex < end && localRun < _richRuns.Count)
				{
					RichTextRun run = _richRuns[localRun];
					int segStart = Math.Max(posIndex, run.Start);
					int segEnd = Math.Min(end, run.End);

					if (segEnd <= segStart)
					{
						localRun++;
						continue;
					}

					string segText = text.Substring(segStart, segEnd - segStart);
					if (segText.Length == 0)
					{
						posIndex = segEnd;
						if (run.End <= segEnd)
							localRun++;
						continue;
					}

					TextFormat fmt = run.Format ?? _textFormat;

					Color segColor = fmt.color;
					if (alpha < 1f)
					{
						int a = (int)Math.Round(segColor.A * alpha);
						if (a < 0) a = 0;
						if (a > 255) a = 255;
						segColor = new Color(segColor.R, segColor.G, segColor.B, (byte)a);
					}

					TextStyle segStyle = fmt.underline ? TextStyle.Underline : TextStyle.None;

					Vector2 pos = new Vector2(xPos, line.y);
					spriteFont.DrawText(renderer, segText, pos, segColor, 0f, Vector2.Zero, null, 0f,
						_textFormat.letterSpacing, 0f, segStyle, effect, effectAmount);

					float advance = 0F;
					try
					{
						advance = spriteFont.MeasureString(segText).X;
					}
					catch
					{
						advance = 0F;
					}

					if (segText.Length > 1)
						advance += _textFormat.letterSpacing * (segText.Length - 1);

					xPos += advance;
					posIndex = segEnd;

					if (run.End <= segEnd)
						localRun++;
				}
			}

			// 注意：目前 TextField 仅支持单一字体图集（单 Texture2D）。若 FontStashSharp 自动扩展为多图集，
			// 需要后续实现多纹理拆批或扩大 FontSystem 图集尺寸。
		}

		private void ResolveFont()
		{
			string fontName = _textFormat.font;
			if (string.IsNullOrEmpty(fontName))
				fontName = UIConfig.defaultFont;

			if (_font == null || _font.name != fontName)
				_font = FontManager.GetFont(fontName);

			_font ??= new DynamicFont(fontName);
			_font.SetFormat(_textFormat, _fontSizeScale);
			EnsureFontTextureReady();
		}

		private void EnsureFontTextureReady()
		{
			if (_font is BitmapFont bitmapFont && bitmapFont.mainTexture != null)
			{
				if (graphics.texture != bitmapFont.mainTexture)
					graphics.texture = bitmapFont.mainTexture;
				return;
			}

			FontSystem fontSystem = CMain.fontSystem;
			if (fontSystem == null)
				return;

			int fontSize = _textFormat.size;
			if (_font is DynamicFont dyn && dyn.Size > 0)
				fontSize = dyn.Size;

			fontSize = Math.Max(1, fontSize);

			SpriteFontBase spriteFont = fontSystem.GetFont(fontSize);
			if (spriteFont == null)
				return;

			FontSystemEffect effect = _stroke > 0 ? FontSystemEffect.Stroked : FontSystemEffect.None;
			int effectAmount = _stroke > 0 ? _stroke : 0;

			// 强制生成字形：确保字体图集与纹理已创建（避免首次渲染时 texture 为空）。
			string sample = string.IsNullOrEmpty(_text) ? " " : _text;
			spriteFont.GetGlyphs(sample, Vector2.Zero, Vector2.Zero, null, _textFormat.letterSpacing, _textFormat.lineSpacing, effect, effectAmount);

			DynamicSpriteFont dynamicSpriteFont = spriteFont as DynamicSpriteFont;
			Texture2D atlasTexture = dynamicSpriteFont != null && dynamicSpriteFont.FontSystem.Atlases.Count > 0
				? dynamicSpriteFont.FontSystem.Atlases[0].Texture
				: null;

			if (atlasTexture == null)
				return;

			NTexture ntex = GetOrCreateFontTexture(atlasTexture);
			if (graphics.texture != ntex)
				graphics.texture = ntex;
		}

		private void CleanupHtmlElementsAndObjects()
		{
			if (_elements.Count == 0)
				return;

			try
			{
				_richTextField?.CleanupObjects();
			}
			catch
			{
			}

			try
			{
				HtmlElement.ReturnElements(_elements);
			}
			catch
			{
				_elements.Clear();
			}
		}

		private void BuildRenderTextFromHtml()
		{
			_richRuns.Clear();
			_linkSpans.Clear();

			// 无论是否继续解析，都要先清理旧的 html 对象，避免残留点击区域。
			CleanupHtmlElementsAndObjects();

			if (!_html || string.IsNullOrEmpty(_text))
			{
				_renderText = _text ?? string.Empty;
				return;
			}

			try
			{
				HtmlParser.inst.Parse(_text, _textFormat, _elements, _richTextField != null ? _richTextField.htmlParseOptions : null);
			}
			catch
			{
				_elements.Clear();
				_renderText = _text ?? string.Empty;
				return;
			}

			if (_elements.Count == 0)
			{
				_renderText = string.Empty;
				return;
			}

			var sb = new System.Text.StringBuilder(Math.Max(16, _text.Length));
			var linkStack = new List<HtmlElement>(2);

			for (int i = 0; i < _elements.Count; i++)
			{
				HtmlElement element = _elements[i];
				if (element == null)
					continue;

				switch (element.type)
				{
					case HtmlElementType.Link:
						element.charIndex = sb.Length;
						linkStack.Add(element);
						break;

					case HtmlElementType.LinkEnd:
						if (linkStack.Count > 0)
						{
							HtmlElement link = linkStack[linkStack.Count - 1];
							linkStack.RemoveAt(linkStack.Count - 1);
							_linkSpans.Add(new LinkSpan { Element = link, Start = link.charIndex, End = sb.Length });
						}
						break;

					case HtmlElementType.Text:
						{
							string t = element.text ?? string.Empty;
							if (t.Length == 0)
								break;

							int start = sb.Length;
							sb.Append(t);
							int end = sb.Length;

							TextFormat format = element.format ?? _textFormat;

							if (_richRuns.Count > 0)
							{
								RichTextRun last = _richRuns[_richRuns.Count - 1];
								if (last.End == start && last.Format != null && format != null && last.Format.EqualStyle(format))
								{
									last.End = end;
									_richRuns[_richRuns.Count - 1] = last;
									break;
								}
							}

							_richRuns.Add(new RichTextRun { Start = start, End = end, Format = format });
						}
						break;
				}
			}

			// 未闭合的链接兜底到文本末尾
			for (int i = linkStack.Count - 1; i >= 0; i--)
			{
				HtmlElement link = linkStack[i];
				if (link == null)
					continue;

				_linkSpans.Add(new LinkSpan { Element = link, Start = link.charIndex, End = sb.Length });
			}

			_renderText = sb.ToString();
		}

		private void RefreshHtmlLinkObjects(SpriteFontBase measureFont, Dictionary<char, float> glyphWidthCache)
		{
			if (_richTextField == null || _linkSpans == null || _linkSpans.Count == 0)
				return;

			string text = _renderText ?? string.Empty;
			if (text.Length == 0 || _lines == null || _lines.Count == 0)
				return;

			SpriteFontBase font = measureFont;
			if (font == null)
			{
				FontSystem fontSystem = CMain.fontSystem;
				if (fontSystem == null)
					return;

				int fontSize = _textFormat.size;
				if (_font is DynamicFont dyn && dyn.Size > 0)
					fontSize = dyn.Size;
				fontSize = Math.Max(1, fontSize);

				try
				{
					font = fontSystem.GetFont(fontSize);
				}
				catch
				{
					font = null;
				}

				if (font == null)
					return;

				glyphWidthCache ??= new Dictionary<char, float>(256);
			}

			float lineHeight = Math.Max(_textFormat.size, 1);
			int letterSpacing = _textFormat.letterSpacing;

			float rectWidth = _contentRect.Width - GUTTER_X * 2;
			if (rectWidth < 0)
				rectWidth = 0;

			float MeasureChar(char ch)
			{
				if (glyphWidthCache != null && glyphWidthCache.TryGetValue(ch, out float cached))
					return cached;

				float w = 0F;
				try
				{
					w = font.MeasureString(ch.ToString()).X;
				}
				catch
				{
					w = 0F;
				}

				if (w <= 0F)
					w = EstimateGlyphWidth(ch, lineHeight);

				if (glyphWidthCache != null)
					glyphWidthCache[ch] = w;

				return w;
			}

			int FindLineIndexByCharIndex(int charIndex, bool allowLineEnd)
			{
				if (_lines == null || _lines.Count == 0)
					return 0;

				if (charIndex <= 0)
					return 0;

				for (int i = 0; i < _lines.Count; i++)
				{
					LineInfo line = _lines[i];
					if (line == null)
						continue;

					if (charIndex < line.startCharIndex)
						continue;

					if (charIndex < line.endCharIndex)
						return i;

					if (allowLineEnd && charIndex == line.endCharIndex && line.endCharIndex > line.startCharIndex)
						return i;
				}

				return _lines.Count - 1;
			}

			float GetLineStartX(LineInfo line)
			{
				float startX = GUTTER_X;
				if (_textFormat.align == AlignType.Center)
					startX = GUTTER_X + (rectWidth - line.width) / 2f;
				else if (_textFormat.align == AlignType.Right)
					startX = GUTTER_X + (rectWidth - line.width);

				if (startX < GUTTER_X)
					startX = GUTTER_X;

				return startX;
			}

			float GetCharXInLine(int lineIndex, int charIndex)
			{
				if (lineIndex < 0)
					lineIndex = 0;
				if (lineIndex >= _lines.Count)
					lineIndex = _lines.Count - 1;

				LineInfo line = _lines[lineIndex];
				int start = line.startCharIndex;
				int end = Math.Clamp(charIndex, start, line.endCharIndex);

				float x = GetLineStartX(line);
				bool hasAny = false;

				for (int i = start; i < end && i < text.Length; i++)
				{
					char ch = text[i];
					if (ch == '\r')
						continue;

					if (hasAny)
						x += letterSpacing;

					x += MeasureChar(ch);
					hasAny = true;
				}

				return x;
			}

			for (int i = 0; i < _linkSpans.Count; i++)
			{
				LinkSpan span = _linkSpans[i];
				HtmlElement element = span.Element;
				if (element == null)
					continue;

				int startIndex = Math.Clamp(span.Start, 0, text.Length);
				int endIndex = Math.Clamp(span.End, startIndex, text.Length);
				if (endIndex <= startIndex)
					continue;

				IHtmlObject obj = element.htmlObject;
				if (obj == null)
				{
					try
					{
						obj = _richTextField.htmlPageContext.CreateObject(_richTextField, element);
						element.htmlObject = obj;
					}
					catch
					{
						obj = null;
					}
				}

				if (obj is not HtmlLink link)
					continue;

				int startLine = FindLineIndexByCharIndex(startIndex, allowLineEnd: false);
				int endLine = FindLineIndexByCharIndex(Math.Max(startIndex, endIndex - 1), allowLineEnd: true);

				float startX = GetCharXInLine(startLine, startIndex);
				float endX = GetCharXInLine(endLine, endIndex);

				try
				{
					link.SetArea(startLine, startX, endLine, endX);
					link.SetPosition(this.x, this.y);
					link.Add();
				}
				catch
				{
				}
			}
		}

		private void BuildLines()
		{
			_textChanged = false;
			graphics.SetMeshDirty();

			_globalScale = UIContentScaler.scaleFactor;
			_fontSizeScale = 1f;

			if (_font == null)
				ResolveFont();

			// 解析 HTML/UBB（RichTextField / UBBEnabled 会走 htmlText）
			BuildRenderTextFromHtml();

			LineInfo.Return(_lines);
			_lines.Clear();

			if (_input)
			{
				_charPositions ??= new List<CharPosition>();
				_charPositions.Clear();
				_charPositions.Add(new CharPosition { charIndex = 0, lineIndex = 0, offsetX = GUTTER_X });
			}

			string text = _renderText ?? string.Empty;

			if (string.IsNullOrEmpty(text))
			{
				_lines.Add(new LineInfo { width = 0, height = _textFormat.size, textHeight = _textFormat.size, y2 = GUTTER_Y, y = GUTTER_Y, startCharIndex = 0, endCharIndex = 0 });
				_textWidth = 0;
				_textHeight = 0;
				BuildLinesFinal();
				return;
			}

			int letterSpacing = _textFormat.letterSpacing;
			int lineSpacing = Math.Max(0, _textFormat.lineSpacing);

			float rectWidth = _contentRect.Width - GUTTER_X * 2;
			bool wrap = _wordWrap && !_singleLine;
			if (_maxWidth > 0)
			{
				wrap = true;
				rectWidth = _maxWidth - GUTTER_X * 2;
			}

			rectWidth = Math.Max(0, rectWidth);

			float lineHeight = Math.Max(_textFormat.size, 1);

			// 尝试使用 FontStashSharp 的真实测量，保证换行/链接点击区域更贴近实际渲染
			SpriteFontBase measureFont = null;
			FontSystem fontSystem = CMain.fontSystem;
			try
			{
				if (fontSystem != null && _font is not BitmapFont)
				{
					int fontSize = _textFormat.size;
					if (_font is DynamicFont dyn && dyn.Size > 0)
						fontSize = dyn.Size;
					fontSize = Math.Max(1, fontSize);
					measureFont = fontSystem.GetFont(fontSize);
				}
			}
			catch
			{
				measureFont = null;
			}

			Dictionary<char, float> glyphWidthCache = measureFont != null ? new Dictionary<char, float>(256) : null;
			float MeasureGlyphWidth(char ch)
			{
				if (measureFont == null)
					return EstimateGlyphWidth(ch, lineHeight);

				if (glyphWidthCache.TryGetValue(ch, out float cached))
					return cached;

				float w = 0F;
				try
				{
					w = measureFont.MeasureString(ch.ToString()).X;
				}
				catch
				{
					w = 0F;
				}

				if (w <= 0F)
					w = EstimateGlyphWidth(ch, lineHeight);

				glyphWidthCache[ch] = w;
				return w;
			}
			float x = GUTTER_X;
			float y = GUTTER_Y;
			int lineIndex = 0;
			float lineWidth = 0;

			var line = new LineInfo { y2 = y, y = y, height = lineHeight, textHeight = lineHeight, width = 0, startCharIndex = 0, endCharIndex = 0 };
			_lines.Add(line);

			for (int i = 0; i < text.Length; i++)
			{
				char ch = text[i];
				if (ch == '\r')
					continue;

				if (ch == '\n')
				{
					line.width = lineWidth;
					line.endCharIndex = i;
					StartNewLine(ref line, ref lineIndex, ref x, ref y, lineHeight, lineSpacing, i + 1);
					lineWidth = 0;
					continue;
				}

				float glyphWidth = MeasureGlyphWidth(ch);
				float nextWidth = (lineWidth == 0 ? glyphWidth : (lineWidth + letterSpacing + glyphWidth));

				if (wrap && rectWidth > 0 && nextWidth > rectWidth && lineWidth > 0)
				{
					line.width = lineWidth;
					line.endCharIndex = i;
					StartNewLine(ref line, ref lineIndex, ref x, ref y, lineHeight, lineSpacing, i);
					lineWidth = 0;
				}

				if (lineWidth != 0)
				{
					x += letterSpacing;
					lineWidth += letterSpacing;
				}

				x += glyphWidth;
				lineWidth += glyphWidth;

				if (_input)
				{
					_charPositions.Add(new CharPosition
					{
						charIndex = _charPositions.Count,
						lineIndex = (short)lineIndex,
						offsetX = (int)Math.Round(x),
					});
				}
			}

			line.width = lineWidth;
			line.endCharIndex = text.Length;

			_textWidth = 0;
			for (int i = 0; i < _lines.Count; i++)
			{
				if (_lines[i].width > _textWidth)
					_textWidth = _lines[i].width;
			}

			_textWidth = _textWidth > 0 ? _textWidth + GUTTER_X * 2 : 0;
			_textHeight = y + lineHeight + GUTTER_Y;

			BuildLinesFinal();

			// 更新富文本链接点击区域
			try
			{
				RefreshHtmlLinkObjects(measureFont, glyphWidthCache);
			}
			catch
			{
			}
		}

		private void StartNewLine(ref LineInfo currentLine, ref int lineIndex, ref float x, ref float y, float lineHeight, int lineSpacing, int startCharIndex)
		{
			lineIndex++;
			x = GUTTER_X;
			y += lineHeight + lineSpacing;
			currentLine = new LineInfo { y2 = y, y = y, height = lineHeight, textHeight = lineHeight, width = 0, startCharIndex = startCharIndex, endCharIndex = startCharIndex };
			_lines.Add(currentLine);

			if (_input)
			{
				_charPositions.Add(new CharPosition
				{
					charIndex = _charPositions.Count,
					lineIndex = (short)lineIndex,
					offsetX = GUTTER_X,
				});
			}
		}

		private static float EstimateGlyphWidth(char ch, float lineHeight)
		{
			// 简化估算：
			// - ASCII 近似半角
			// - 其余按全角处理
			if (ch <= 0x00FF)
			{
				if (ch == ' ')
					return lineHeight * 0.35f;
				return lineHeight * 0.55f;
			}

			return lineHeight;
		}

		private void BuildLinesFinal()
		{
			if (!_input && _autoSize == AutoSizeType.Both)
			{
				_updatingSize = true;
				SetSize(_textWidth, _textHeight);
				_updatingSize = false;
			}
			else if (_autoSize == AutoSizeType.Height)
			{
				_updatingSize = true;
				float h = _textHeight;
				if (_input && h < _minHeight)
					h = _minHeight;
				this.height = h;
				_updatingSize = false;
			}

			_yOffset = 0;
			ApplyVertAlign();
		}

		private void ApplyVertAlign()
		{
			int oldOffset = _yOffset;
			if (_autoSize == AutoSizeType.Both || _autoSize == AutoSizeType.Height || _verticalAlign == VertAlignType.Top)
			{
				_yOffset = 0;
			}
			else
			{
				float dh = _textHeight <= 0 ? (_contentRect.Height - _textFormat.size) : (_contentRect.Height - _textHeight);
				if (dh < 0)
					dh = 0;
				_yOffset = _verticalAlign == VertAlignType.Middle ? (int)(dh / 2) : (int)dh;
			}

			if (oldOffset == _yOffset)
				return;

			for (int i = 0; i < _lines.Count; i++)
				_lines[i].y = _lines[i].y2 + _yOffset;

			graphics.SetMeshDirty();
		}

		public class LineInfo
		{
			public float width;
			public float height;
			public float textHeight;
			public float y;
			public float y2;
			public int startCharIndex;
			public int endCharIndex;

			private static readonly Stack<LineInfo> Pool = new Stack<LineInfo>();

			public static LineInfo Borrow()
			{
				if (Pool.Count > 0)
				{
					LineInfo ret = Pool.Pop();
					ret.width = 0;
					ret.height = 0;
					ret.textHeight = 0;
					ret.y = 0;
					ret.y2 = 0;
					ret.startCharIndex = 0;
					ret.endCharIndex = 0;
					return ret;
				}

				return new LineInfo();
			}

			public static void Return(LineInfo value)
			{
				if (value != null)
					Pool.Push(value);
			}

			public static void Return(List<LineInfo> values)
			{
				if (values == null)
					return;

				for (int i = 0; i < values.Count; i++)
					Pool.Push(values[i]);

				values.Clear();
			}
		}

		public struct CharPosition
		{
			public int charIndex;
			public short lineIndex;
			public int offsetX;
		}
	}
}
