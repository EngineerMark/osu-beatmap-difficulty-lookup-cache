using System.Reflection;
using System;
using System.Linq;

namespace BeatmapDifficultyLookupCache
{
    public static class Helpers
    {
        public static bool TryFindPrivateBaseField<T>(this object obj, string name, out T value)
        {
            Type t = obj.GetType();
            bool found = false;
            value = default(T);
            do
            {
                var field = t.GetFields(BindingFlags.Default | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(f => f.Name == name);
                if (field != default(FieldInfo))
                {
                    value = (T)field.GetValue(obj);
                    found = true;
                }
                else
                {
                    t = t.BaseType;
                }
            } while (!found && t != null);

            return found;
        }

        public static bool TryFindPrivateBaseMethod(this object obj, string name, out MethodInfo? value)
        {
            Type t = obj.GetType();
            bool found = false;
            value = default(MethodInfo);
            do
            {
                var method = t.GetMethods(BindingFlags.Default | BindingFlags.NonPublic | BindingFlags.Instance)
                    .FirstOrDefault(m => m.Name == name);
                if (method != default(MethodInfo))
                {
                    value = method;
                    found = true;
                }
                else
                {
                    t = t.BaseType;
                }
            } while (!found && t != null);
            return found;
        }
    }
}
