using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

public static class Functions
{
    public static bool CompareBytes(byte[] a, byte[] b)
    {
        if (a == b) return true;

        if (a == null || b == null || a.Length != b.Length) return false;

        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;

        return true;
    }

    public static string ConvertByteSize(double byteCount)
    {
        string size = "0 Bytes";
        if (byteCount >= 1073741824.0)
            size = string.Format("{0:##.##}", byteCount / 1073741824.0) + " GB";
        else if (byteCount >= 1048576.0)
            size = string.Format("{0:##.##}", byteCount / 1048576.0) + " MB";
        else if (byteCount >= 1024.0)
            size = string.Format("{0:##.##}", byteCount / 1024.0) + " KB";
        else if (byteCount > 0 && byteCount < 1024.0)
            size = byteCount + " Bytes";

        return size;
    }

    public static bool TryParse(string s, out Point temp)
    {
        temp = Point.Empty;

        if (string.IsNullOrWhiteSpace(s)) return false;

        string[] data = s.Split(',');
        if (data.Length <= 1) return false;

        if (!int.TryParse(data[0], out int tempX))
            return false;

        if (!int.TryParse(data[1], out int tempY))
            return false;

        temp = new Point(tempX, tempY);
        return true;
    }

    public static Point Subtract(this Point p1, Point p2)
    {
        return new Point(p1.X - p2.X, p1.Y - p2.Y);
    }

    public static Point Subtract(this Point p1, int x, int y)
    {
        return new Point(p1.X - x, p1.Y - y);
    }

    public static Point Add(this Point p1, Point p2)
    {
        return new Point(p1.X + p2.X, p1.Y + p2.Y);
    }

    public static Point Add(this Point p1, int x, int y)
    {
        return new Point(p1.X + x, p1.Y + y);
    }

    public static string PointToString(Point p)
    {
        return string.Format("{0}, {1}", p.X, p.Y);
    }

    public static bool InRange(Point a, Point b, int i)
    {
        return Math.Abs(a.X - b.X) <= i && Math.Abs(a.Y - b.Y) <= i;
    }

    public static bool FacingEachOther(MirDirection dirA, Point pointA, MirDirection dirB, Point pointB)
    {
        return dirA == DirectionFromPoint(pointA, pointB) && dirB == DirectionFromPoint(pointB, pointA);
    }

    public static string PrintTimeSpanFromSeconds(double secs, bool accurate = true)
    {
        TimeSpan t = TimeSpan.FromSeconds(secs);
        string answer;
        if (t.TotalMinutes < 1.0)
        {
            answer = string.Format("{0}秒", t.Seconds);
        }
        else if (t.TotalHours < 1.0)
        {
            answer = accurate ? string.Format("{0}分 {1:D2}秒", t.Minutes, t.Seconds) : string.Format("{0}分", t.Minutes);
        }
        else if (t.TotalDays < 1.0)
        {
            answer = accurate ? string.Format("{0}时 {1:D2}分 {2:D2}秒", (int)t.Hours, t.Minutes, t.Seconds) : string.Format("{0}时 {1:D2}分", (int)t.TotalHours, t.Minutes);
        }
        else
        {
            answer = accurate ? string.Format("{0}天 {1:D2}时 {2:D2}分 {3:D2}秒", (int)t.Days, (int)t.Hours, t.Minutes, t.Seconds) : string.Format("{0}天 {1}时 {2:D2}分", (int)t.TotalDays, (int)t.Hours, t.Minutes);
        }

        return answer;
    }

    public static string PrintTimeSpanFromMilliSeconds(double milliSeconds)
    {
        TimeSpan t = TimeSpan.FromMilliseconds(milliSeconds);
        string answer;
        if (t.TotalMinutes < 1.0)
        {
            answer = string.Format("{0}.{1}秒", t.Seconds, (decimal)(t.Milliseconds / 100));
        }
        else if (t.TotalHours < 1.0)
        {
            answer = string.Format("{0}分 {1:D2}秒", t.TotalMinutes, t.Seconds);
        }
        else if (t.TotalDays < 1.0)
        {
            answer = string.Format("{0}时 {1:D2}分 {2:D2}秒", (int)t.TotalHours, t.Minutes, t.Seconds);
        }
        else
        {
            answer = string.Format("{0}天 {1}时 {2:D2}分 {3:D2}秒", (int)t.Days, (int)t.Hours, t.Minutes, t.Seconds);
        }

        return answer;
    }

    public static MirDirection PreviousDir(MirDirection d)
    {
        switch (d)
        {
            case MirDirection.Up: return MirDirection.UpLeft;
            case MirDirection.UpRight: return MirDirection.Up;
            case MirDirection.Right: return MirDirection.UpRight;
            case MirDirection.DownRight: return MirDirection.Right;
            case MirDirection.Down: return MirDirection.DownRight;
            case MirDirection.DownLeft: return MirDirection.Down;
            case MirDirection.Left: return MirDirection.DownLeft;
            case MirDirection.UpLeft: return MirDirection.Left;
            default: return d;
        }
    }

    public static MirDirection NextDir(MirDirection d)
    {
        switch (d)
        {
            case MirDirection.Up: return MirDirection.UpRight;
            case MirDirection.UpRight: return MirDirection.Right;
            case MirDirection.Right: return MirDirection.DownRight;
            case MirDirection.DownRight: return MirDirection.Down;
            case MirDirection.Down: return MirDirection.DownLeft;
            case MirDirection.DownLeft: return MirDirection.Left;
            case MirDirection.Left: return MirDirection.UpLeft;
            case MirDirection.UpLeft: return MirDirection.Up;
            default: return d;
        }
    }

    public static MirDirection DirectionFromPoint(Point source, Point dest)
    {
        if (source.X < dest.X)
        {
            if (source.Y < dest.Y) return MirDirection.DownRight;
            if (source.Y > dest.Y) return MirDirection.UpRight;
            return MirDirection.Right;
        }

        if (source.X > dest.X)
        {
            if (source.Y < dest.Y) return MirDirection.DownLeft;
            if (source.Y > dest.Y) return MirDirection.UpLeft;
            return MirDirection.Left;
        }

        return source.Y < dest.Y ? MirDirection.Down : MirDirection.Up;
    }

    public static MirDirection ShiftDirection(MirDirection dir, int i)
    {
        return (MirDirection)(((int)dir + i + 8) % 8);
    }

    public static Size Add(this Size p1, Size p2)
    {
        return new Size(p1.Width + p2.Width, p1.Height + p2.Height);
    }

    public static Size Add(this Size p1, int width, int height)
    {
        return new Size(p1.Width + width, p1.Height + height);
    }

    public static Point PointMove(Point p, MirDirection d, int i)
    {
        switch (d)
        {
            case MirDirection.Up: p.Offset(0, -i); break;
            case MirDirection.UpRight: p.Offset(i, -i); break;
            case MirDirection.Right: p.Offset(i, 0); break;
            case MirDirection.DownRight: p.Offset(i, i); break;
            case MirDirection.Down: p.Offset(0, i); break;
            case MirDirection.DownLeft: p.Offset(-i, i); break;
            case MirDirection.Left: p.Offset(-i, 0); break;
            case MirDirection.UpLeft: p.Offset(-i, -i); break;
        }
        return p;
    }

    public static Point Left(Point p, MirDirection d)
    {
        switch (d)
        {
            case MirDirection.Up: p.Offset(-1, 0); break;
            case MirDirection.UpRight: p.Offset(-1, -1); break;
            case MirDirection.Right: p.Offset(0, -1); break;
            case MirDirection.DownRight: p.Offset(1, -1); break;
            case MirDirection.Down: p.Offset(1, 0); break;
            case MirDirection.DownLeft: p.Offset(1, 1); break;
            case MirDirection.Left: p.Offset(0, 1); break;
            case MirDirection.UpLeft: p.Offset(-1, 1); break;
        }
        return p;
    }

    public static Point Right(Point p, MirDirection d)
    {
        switch (d)
        {
            case MirDirection.Up: p.Offset(1, 0); break;
            case MirDirection.UpRight: p.Offset(1, 1); break;
            case MirDirection.Right: p.Offset(0, 1); break;
            case MirDirection.DownRight: p.Offset(-1, 1); break;
            case MirDirection.Down: p.Offset(-1, 0); break;
            case MirDirection.DownLeft: p.Offset(-1, -1); break;
            case MirDirection.Left: p.Offset(0, -1); break;
            case MirDirection.UpLeft: p.Offset(1, -1); break;
        }
        return p;
    }

    public static int MaxDistance(Point p1, Point p2)
    {
        return Math.Max(Math.Abs(p1.X - p2.X), Math.Abs(p1.Y - p2.Y));
    }

    public static MirDirection ReverseDirection(MirDirection dir)
    {
        switch (dir)
        {
            case MirDirection.Up: return MirDirection.Down;
            case MirDirection.UpRight: return MirDirection.DownLeft;
            case MirDirection.Right: return MirDirection.Left;
            case MirDirection.DownRight: return MirDirection.UpLeft;
            case MirDirection.Down: return MirDirection.Up;
            case MirDirection.DownLeft: return MirDirection.UpRight;
            case MirDirection.Left: return MirDirection.Right;
            case MirDirection.UpLeft: return MirDirection.DownRight;
            default: return dir;
        }
    }

    public static ItemInfo GetRealItem(ItemInfo origin, ushort level, MirClass job, List<ItemInfo> itemList)
    {
        if (origin.ClassBased && origin.LevelBased)
            return GetClassAndLevelBasedItem(origin, job, level, itemList);
        if (origin.ClassBased)
            return GetClassBasedItem(origin, job, itemList);
        if (origin.LevelBased)
            return GetLevelBasedItem(origin, level, itemList);
        return origin;
    }

    public static ItemInfo GetLevelBasedItem(ItemInfo origin, ushort level, List<ItemInfo> itemList)
    {
        ItemInfo output = origin;
        for (int i = 0; i < itemList.Count; i++)
        {
            ItemInfo info = itemList[i];
            if (info.Name.StartsWith(origin.Name) &&
                info.RequiredType == RequiredType.Level &&
                info.RequiredAmount <= level &&
                output.RequiredAmount < info.RequiredAmount &&
                origin.RequiredGender == info.RequiredGender)
            {
                output = info;
            }
        }
        return output;
    }

    public static ItemInfo GetClassBasedItem(ItemInfo origin, MirClass job, List<ItemInfo> itemList)
    {
        for (int i = 0; i < itemList.Count; i++)
        {
            ItemInfo info = itemList[i];
            if (info.Name.StartsWith(origin.Name) &&
                (byte)info.RequiredClass == (1 << (byte)job) &&
                origin.RequiredGender == info.RequiredGender)
            {
                return info;
            }
        }
        return origin;
    }

    public static ItemInfo GetClassAndLevelBasedItem(ItemInfo origin, MirClass job, ushort level, List<ItemInfo> itemList)
    {
        ItemInfo output = origin;
        for (int i = 0; i < itemList.Count; i++)
        {
            ItemInfo info = itemList[i];
            if (info.Name.StartsWith(origin.Name) &&
                (byte)info.RequiredClass == (1 << (byte)job) &&
                info.RequiredType == RequiredType.Level &&
                info.RequiredAmount <= level &&
                output.RequiredAmount <= info.RequiredAmount &&
                origin.RequiredGender == info.RequiredGender)
            {
                output = info;
            }
        }
        return output;
    }

    public static string StringOverLines(string line, int maxWordsPerLine, int maxLettersPerLine)
    {
        string newString = string.Empty;
        string[] words = line.Split(' ');
        int lineLength = 0;

        for (int i = 0; i < words.Length; i++)
        {
            lineLength += words[i].Length + 1;
            newString += words[i] + " ";
            if (i > 0 && i % maxWordsPerLine == 0 && lineLength > maxLettersPerLine)
            {
                lineLength = 0;
                newString += "\r\n";
            }
        }

        return newString;
    }

    public static IEnumerable<byte[]> SplitArray(byte[] value, int bufferLength)
    {
        int countOfArray = value.Length / bufferLength;
        if (value.Length % bufferLength > 0)
            countOfArray++;

        for (int i = 0; i < countOfArray; i++)
            yield return value.Skip(i * bufferLength).Take(bufferLength).ToArray();
    }

    public static byte[] CombineArray(List<byte[]> arrays)
    {
        byte[] result = new byte[arrays.Sum(x => x.Length)];
        int offset = 0;
        foreach (byte[] array in arrays)
        {
            Buffer.BlockCopy(array, 0, result, offset, array.Length);
            offset += array.Length;
        }
        return result;
    }

    public static List<List<T>> SplitList<T>(int width, List<T> originalList)
    {
        var chunks = new List<List<T>>();

        if (width == 0)
        {
            chunks.Add(originalList);
            return chunks;
        }

        int numberOfLists = originalList.Count / width;

        for (int i = 0; i <= numberOfLists; i++)
            chunks.Add(originalList.Skip(i * width).Take(width).ToList());

        return chunks;
    }

    public static byte[] CompressBytes(byte[] raw)
    {
        using (var memory = new MemoryStream())
        {
            using (var gzip = new GZipStream(memory, CompressionMode.Compress, true))
            {
                gzip.Write(raw, 0, raw.Length);
            }

            return memory.ToArray();
        }
    }

    public static byte[] DecompressBytes(byte[] gzip)
    {
        using (var stream = new GZipStream(new MemoryStream(gzip), CompressionMode.Decompress))
        using (var memory = new MemoryStream())
        {
            var buffer = new byte[4096];
            int count;
            while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                memory.Write(buffer, 0, count);

            return memory.ToArray();
        }
    }

    public static byte[] SerializeToBytes<T>(T item)
    {
        return JsonSerializer.SerializeToUtf8Bytes(item);
    }

    public static object DeserializeFromBytes(byte[] bytes)
    {
        return JsonSerializer.Deserialize(bytes, typeof(object));
    }

    public static T DeserializeFromBytes<T>(byte[] bytes)
    {
        return JsonSerializer.Deserialize<T>(bytes);
    }
}
