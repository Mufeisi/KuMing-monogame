using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Reflection;

internal static class HelperExtensionCore
{
    private static readonly Random Rng = new Random();

    public static T ValueOrDefault<T>(object value)
    {
        if (value == null || value.ToString() == "")
            return default;

        return (T)Convert.ChangeType(value, typeof(T));
    }

    public static void Shuffle<T>(IList<T> list)
    {
        int n = list.Count;
        while (n > 1)
        {
            n--;
            int k = Rng.Next(n + 1);
            T current = list[k];
            list[k] = list[n];
            list[n] = current;
        }
    }

    public static string ToEnumString(Enum value)
    {
        FieldInfo enumInfo = value.GetType().GetField(value.ToString());
        if (enumInfo == null)
            return string.Empty;

        DescriptionAttribute[] attributes =
            (DescriptionAttribute[])enumInfo.GetCustomAttributes(typeof(DescriptionAttribute), false);

        return attributes.Length > 0 ? attributes[0].Description : value.ToString();
    }

    public static T ToEnumByDescription<T>(string description) where T : Enum
    {
        FieldInfo[] fields = typeof(T).GetFields();
        foreach (FieldInfo field in fields)
        {
            object[] attributes = field.GetCustomAttributes(typeof(DescriptionAttribute), false);
            if (attributes.Length > 0 && (attributes[0] as DescriptionAttribute)?.Description == description)
                return (T)field.GetValue(null);
        }

        return default;
    }
}

namespace Shared.Extensions
{
    public static class HelperExtensions
    {
        public static T ValueOrDefault<T>(this object value)
        {
            return HelperExtensionCore.ValueOrDefault<T>(value);
        }

        public static void Shuffle<T>(this IList<T> list)
        {
            HelperExtensionCore.Shuffle(list);
        }

        public static string ToEnumString(this Enum value)
        {
            return HelperExtensionCore.ToEnumString(value);
        }

        public static T ToEnumByDescription<T>(this string description) where T : Enum
        {
            return HelperExtensionCore.ToEnumByDescription<T>(description);
        }
    }
}

namespace MonoShare.Share.Extensions
{
    public static class HelperExtensions
    {
        public static T ValueOrDefault<T>(this object value)
        {
            return HelperExtensionCore.ValueOrDefault<T>(value);
        }

        public static void Shuffle<T>(this IList<T> list)
        {
            HelperExtensionCore.Shuffle(list);
        }

        public static string ToEnumString(this Enum value)
        {
            return HelperExtensionCore.ToEnumString(value);
        }

        public static T ToEnumByDescription<T>(this string description) where T : Enum
        {
            return HelperExtensionCore.ToEnumByDescription<T>(description);
        }

#if MONOSHARE_XNA
        public static Point ToDrawPoint(this Microsoft.Xna.Framework.Point point)
        {
            return new Point(point.X, point.Y);
        }

        public static Microsoft.Xna.Framework.Point ToXnaPoint(this Point point)
        {
            return new Microsoft.Xna.Framework.Point(point.X, point.Y);
        }

        public static Microsoft.Xna.Framework.Color ToXnaColor(this Color color)
        {
            return new Microsoft.Xna.Framework.Color(color.R, color.G, color.B, color.A);
        }
#endif
    }
}
