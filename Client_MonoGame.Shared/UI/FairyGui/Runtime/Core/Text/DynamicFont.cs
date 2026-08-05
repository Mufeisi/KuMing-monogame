using System;

namespace FairyGUI
{
	/// <summary>
	/// 动态字体（跨平台占位实现）。
	///
	/// 说明：
	/// - 原示例工程使用 System.Drawing.Font（Android/iOS 不可用）。
	/// - 本项目移动端后续将以 FontStashSharp（hm.ttf）实现真正的动态字体渲染。
	/// - 现阶段仅保留必要的“格式状态”，供 TextField/FontManager 编译通过与后续扩展。
	/// </summary>
	public sealed class DynamicFont : BaseFont
	{
		private int _size;
		private bool _bold;
		private bool _italic;
		private bool _underline;

		public DynamicFont(string name)
		{
			this.name = name ?? string.Empty;
		}

		public int Size => _size;
		public bool Bold => _bold;
		public bool Italic => _italic;
		public bool Underline => _underline;

		public override void SetFormat(TextFormat format, float fontSizeScale)
		{
			if (format == null)
				throw new ArgumentNullException(nameof(format));

			_size = fontSizeScale == 1
				? format.size
				: (int)Math.Floor(format.size * fontSizeScale);

			_bold = format.bold;
			_italic = format.italic;
			_underline = format.underline;
		}
	}
}

