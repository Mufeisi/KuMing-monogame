using System;
using System.Collections.Generic;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;
using System.IO;
using System.Text;

public class InIReader
{
    #region Fields
    private List<string> _contents;
    private readonly string _fileName;
    private Exception _loadException;
    private Encoding _encoding = new UTF8Encoding(false);
    private bool _hasBom;
    private string _newline = Environment.NewLine;
    private bool _trailingNewline;

    private sealed class FileSnapshot
    {
        public List<string> Lines { get; set; }
        public Encoding Encoding { get; set; }
        public bool HasBom { get; set; }
        public string Newline { get; set; }
        public bool TrailingNewline { get; set; }
    }
    #endregion

    #region Constructor
    public InIReader(string fileName)
    {
        _fileName = fileName;

        if (!Directory.Exists(Path.GetDirectoryName(fileName)))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fileName));
        }

        _contents = new List<string>();
        try
        {
            var snapshot = ReadFileSnapshot();
            _contents = snapshot.Lines;
            ApplyFileFormat(snapshot);
        }
        catch (Exception exception)
        {
            // 保留读取故障，避免把受锁定/损坏的文件永久当作空配置。
            _loadException = exception;
        }
    }
    #endregion

    #region Functions
    private string FindValue(string section, string key)
    {
        for (int a = 0; a < _contents.Count; a++)
            if (String.CompareOrdinal(_contents[a], "[" + section + "]") == 0)
                for (int b = a + 1; b < _contents.Count; b++)
                    if (String.CompareOrdinal(_contents[b].Split('=')[0], key) == 0)
                        return _contents[b].Split('=')[1];
                    else if (_contents[b].StartsWith("[") && _contents[b].EndsWith("]"))
                        return null;
        return null;
    }

    private int FindIndex(string section, string key)
    {
        for (int a = 0; a < _contents.Count; a++)
            if (String.CompareOrdinal(_contents[a], "[" + section + "]") == 0)
                for (int b = a + 1; b < _contents.Count; b++)
                    if (String.CompareOrdinal(_contents[b].Split('=')[0], key) == 0)
                        return b;
                    else if (_contents[b].StartsWith("[") && _contents[b].EndsWith("]"))
                    {
                        _contents.Insert(b - 1, key + "=");
                        return b - 1;
                    }
                    else if (_contents.Count - 1 == b)
                    {
                        _contents.Add(key + "=");
                        return _contents.Count - 1;
                    }
        if (_contents.Count > 0)
            _contents.Add("");

        _contents.Add("[" + section + "]");
        _contents.Add(key + "=");
        return _contents.Count - 1;
    }

    public void Save()
    {
        try
        {
            File.WriteAllLines(_fileName, _contents);
        }
        catch
        {
        }
    }

    /// <summary>
    /// 删除指定节内所有同名键，并以一次写盘完成密码类配置清理。
    /// 该专用入口不吞写盘异常，调用方可将失败交给现有启动日志处理。
    /// </summary>
    public int ClearKeys(string section, params string[] keys)
    {
        if (string.IsNullOrEmpty(section) || keys == null || keys.Length == 0)
            return 0;

        FileSnapshot snapshot;
        try
        {
            // 清理必须以磁盘最新快照为准，构造阶段失败也可在解除锁后重试。
            snapshot = ReadFileSnapshot();
        }
        catch (Exception exception)
        {
            _loadException = exception;
            throw;
        }

        var updatedContents = new List<string>(snapshot.Lines);
        var removed = 0;
        string currentSection = null;
        for (var i = 0; i < updatedContents.Count;)
        {
            string line = updatedContents[i] ?? string.Empty;
            if (line.Length >= 2 && line[0] == '[' && line[^1] == ']')
            {
                currentSection = line.Substring(1, line.Length - 2);
                i++;
                continue;
            }

            if (!string.Equals(currentSection, section, StringComparison.Ordinal))
            {
                i++;
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0)
            {
                i++;
                continue;
            }

            string key = line.Substring(0, separator);
            var matched = false;
            for (var keyIndex = 0; keyIndex < keys.Length; keyIndex++)
            {
                if (string.Equals(key, keys[keyIndex], StringComparison.Ordinal))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                i++;
                continue;
            }

            updatedContents.RemoveAt(i);
            removed++;
        }

        if (removed == 0)
        {
            _contents = updatedContents;
            ApplyFileFormat(snapshot);
            _loadException = null;
            return 0;
        }

        string temporaryFile = null;
        try
        {
            temporaryFile = WriteSensitiveSnapshot(snapshot, updatedContents);
            ReplaceSensitiveFile(temporaryFile);
            temporaryFile = null;
            _contents = updatedContents;
            ApplyFileFormat(snapshot);
            _loadException = null;
        }
        finally
        {
            if (!string.IsNullOrEmpty(temporaryFile))
            {
                try
                {
                    File.Delete(temporaryFile);
                }
                catch
                {
                    // 保留原始替换异常；临时文件清理失败不吞掉清理主流程错误。
                }
            }
        }

        return removed;
    }

    private FileSnapshot ReadFileSnapshot()
    {
        byte[] bytes;
        try
        {
            using (var stream = new FileStream(
                _fileName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.SequentialScan))
            using (var buffer = new MemoryStream())
            {
                stream.CopyTo(buffer);
                bytes = buffer.ToArray();
            }
        }
        catch (FileNotFoundException)
        {
            return new FileSnapshot
            {
                Lines = new List<string>(),
                Encoding = new UTF8Encoding(false),
                HasBom = false,
                Newline = Environment.NewLine,
                TrailingNewline = false,
            };
        }
        catch (DirectoryNotFoundException)
        {
            return new FileSnapshot
            {
                Lines = new List<string>(),
                Encoding = new UTF8Encoding(false),
                HasBom = false,
                Newline = Environment.NewLine,
                TrailingNewline = false,
            };
        }

        Encoding encoding = new UTF8Encoding(false);
        var hasBom = false;
        var offset = 0;
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(true);
            hasBom = true;
            offset = 3;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = new UnicodeEncoding(false, true);
            hasBom = true;
            offset = 2;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = new UnicodeEncoding(true, true);
            hasBom = true;
            offset = 2;
        }

        var text = encoding.GetString(bytes, offset, bytes.Length - offset);
        var newline = DetectNewline(text);
        var trailingNewline = text.EndsWith("\r", StringComparison.Ordinal)
            || text.EndsWith("\n", StringComparison.Ordinal);
        var lines = new List<string>(text.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None));
        if (trailingNewline && lines.Count > 0 && lines[lines.Count - 1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        return new FileSnapshot
        {
            Lines = lines,
            Encoding = encoding,
            HasBom = hasBom,
            Newline = newline,
            TrailingNewline = trailingNewline,
        };
    }

    private static string DetectNewline(string text)
    {
        var crlf = text.IndexOf("\r\n", StringComparison.Ordinal);
        var lf = text.IndexOf('\n');
        var cr = text.IndexOf('\r');
        if (crlf >= 0 && (lf < 0 || crlf <= lf))
            return "\r\n";
        if (lf >= 0 && (cr < 0 || lf <= cr))
            return "\n";
        if (cr >= 0)
            return "\r";
        return Environment.NewLine;
    }

    private void ApplyFileFormat(FileSnapshot snapshot)
    {
        _encoding = snapshot.Encoding;
        _hasBom = snapshot.HasBom;
        _newline = snapshot.Newline;
        _trailingNewline = snapshot.TrailingNewline;
    }

    private string WriteSensitiveSnapshot(FileSnapshot snapshot, List<string> contents)
    {
        var directory = Path.GetDirectoryName(_fileName);
        if (string.IsNullOrEmpty(directory))
            directory = Directory.GetCurrentDirectory();

        string temporaryFile;
        do
        {
            temporaryFile = Path.Combine(
                directory,
                "." + Path.GetFileName(_fileName) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        }
        while (File.Exists(temporaryFile));

        var text = string.Join(snapshot.Newline, contents);
        if (snapshot.TrailingNewline)
            text += snapshot.Newline;

        using (var stream = new FileStream(
            temporaryFile,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough))
        {
            if (snapshot.HasBom)
            {
                var preamble = snapshot.Encoding.GetPreamble();
                stream.Write(preamble, 0, preamble.Length);
            }

            var bytes = snapshot.Encoding.GetBytes(text);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }

        return temporaryFile;
    }

    private void ReplaceSensitiveFile(string temporaryFile)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                File.Replace(temporaryFile, _fileName, null, true);
                return;
            }
            catch (PlatformNotSupportedException)
            {
                // 极少数 Windows 兼容文件系统不提供 Replace，退回同卷覆盖移动。
            }
        }

        File.Move(temporaryFile, _fileName, true);
    }
    #endregion

    #region Read
    public bool ReadBoolean(string section, string key, bool Default, bool writeWhenNull = true)
    {
        bool result;

        if (!bool.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }

        return result;
    }

    public byte ReadByte(string section, string key, byte Default, bool writeWhenNull = true)
    {
        byte result;

        if (!byte.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }


        return result;
    }

    public sbyte ReadSByte(string section, string key, sbyte Default, bool writeWhenNull = true)
    {
        sbyte result;

        if (!sbyte.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }


        return result;
    }

    public ushort ReadUInt16(string section, string key, ushort Default, bool writeWhenNull = true)
    {
        ushort result;

        if (!ushort.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }


        return result;
    }

    public short ReadInt16(string section, string key, short Default, bool writeWhenNull = true)
    {
        short result;

        if (!short.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }


        return result;
    }

    public uint ReadUInt32(string section, string key, uint Default, bool writeWhenNull = true)
    {
        uint result;

        if (!uint.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }

        return result;
    }

    public int ReadInt32(string section, string key, int Default, bool writeWhenNull = true)
    {
        int result;

        if (!int.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }

        return result;
    }

    public ulong ReadUInt64(string section, string key, ulong Default, bool writeWhenNull = true)
    {
        ulong result;

        if (!ulong.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }

        return result;
    }

    public long ReadInt64(string section, string key, long Default, bool writeWhenNull = true)
    {
        long result;

        if (!long.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }


        return result;
    }

    public float ReadSingle(string section, string key, float Default, bool writeWhenNull = true)
    {
        float result;

        if (!float.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }

        return result;
    }

    public double ReadDouble(string section, string key, double Default, bool writeWhenNull = true)
    {
        double result;

        if (!double.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }

        return result;
    }

    public decimal ReadDecimal(string section, string key, decimal Default, bool writeWhenNull = true)
    {
        decimal result;

        if (!decimal.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }

        return result;
    }

    public string ReadString(string section, string key, string Default, bool writeWhenNull = true)
    {
        string result = FindValue(section, key);

        if (string.IsNullOrEmpty(result))
        {
            result = Default;

            if (writeWhenNull) Write(section, key, Default);
        }

        return result;
    }

    public char ReadChar(string section, string key, char Default, bool writeWhenNull = true)
    {
        char result;

        if (!char.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            if (writeWhenNull) Write(section, key, Default);
        }

        return result;
    }

    public DrawingPoint ReadPoint(string section, string key, DrawingPoint Default)
    {
        string temp = FindValue(section, key);
        int tempX, tempY;
        if (temp == null || !int.TryParse(temp.Split(',')[0], out tempX))
        {
            Write(section, key, Default);
            return Default;
        }
        if (!int.TryParse(temp.Split(',')[1], out tempY))
        {
            Write(section, key, Default);
            return Default;
        }

        return new DrawingPoint(tempX, tempY);
    }

    public DrawingSize ReadSize(string section, string key, DrawingSize Default)
    {
        string temp = FindValue(section, key);
        int tempX, tempY;
        if (!int.TryParse(temp.Split(',')[0], out tempX))
        {
            Write(section, key, Default);
            return Default;
        }
        if (!int.TryParse(temp.Split(',')[1], out tempY))
        {
            Write(section, key, Default);
            return Default;
        }

        return new DrawingSize(tempX, tempY);
    }

    public TimeSpan ReadTimeSpan(string section, string key, TimeSpan Default)
    {
        TimeSpan result;

        if (!TimeSpan.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            Write(section, key, Default);
        }


        return result;
    }

    public float ReadFloat(string section, string key, float Default)
    {
        float result;

        if (!float.TryParse(FindValue(section, key), out result))
        {
            result = Default;
            Write(section, key, Default);
        }

        return result;
    }
    #endregion

    #region Write
    public void Write(string section, string key, bool value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, byte value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, sbyte value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, ushort value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, short value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, uint value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, int value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, ulong value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, long value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, float value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, double value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, decimal value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, string value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, char value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }

    public void Write(string section, string key, DrawingPoint value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value.X + "," + value.Y;
        Save();
    }

    public void Write(string section, string key, DrawingSize value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value.Width + "," + value.Height;
        Save();
    }

    public void Write(string section, string key, TimeSpan value)
    {
        _contents[FindIndex(section, key)] = key + "=" + value;
        Save();
    }
    #endregion
}
