using System;

namespace Tutan.Messages
{
    /// <summary>
    /// Resolves a serialized message type name — the <c>AssemblyQualifiedName</c>
    /// stored by <see cref="EventTypeAttribute"/>/<see cref="CommandTypeAttribute"/>
    /// fields and by <see cref="MessageReference"/> — back to a <see cref="Type"/>,
    /// tolerating assembly-identity drift.
    /// <para>
    /// A bare <see cref="Type.GetType(string,bool)"/> returns null once the type's
    /// assembly has been renamed (e.g. the script moved into or out of an
    /// <c>.asmdef</c>, or from <c>Assembly-CSharp</c> into a package) or its
    /// strong-name identity changed. On that miss this resolver strips the
    /// assembly part and looks the namespace-qualified name up in every loaded
    /// assembly. Note that <see cref="System.Reflection.Assembly.GetType(string,bool)"/>
    /// rejects assembly-qualified input (it returns null), so the stripping is
    /// required — passing the stored string straight through never matches.
    /// </para>
    /// <para>
    /// Cost: one <c>Type.GetType</c> on the happy path; on a miss, one lookup per
    /// loaded assembly. Not cached — call it at load/authoring time, not per frame.
    /// If two loaded assemblies declare the same full type name, the first match in
    /// <see cref="AppDomain.GetAssemblies"/> order wins.
    /// </para>
    /// </summary>
    public static class MessageTypeResolver
    {
        /// <summary>
        /// Resolve <paramref name="typeName"/> (assembly-qualified or plain full
        /// name) to a <see cref="Type"/>. Returns null when it cannot be found;
        /// never throws for malformed input.
        /// </summary>
        public static Type Resolve(string typeName)
        {
            if (string.IsNullOrEmpty(typeName)) return null;

            Type found;
            try { found = Type.GetType(typeName, false); }
            catch (Exception) { found = null; } // malformed names can still throw (e.g. bad generic syntax)
            if (found != null) return found;

            string fullName = StripAssemblyName(typeName);
            if (fullName.Length == 0) return null;

            // AppDomain.GetAssemblies is deliberate: on the 6000.3 minimum it is the
            // only public loaded-assembly API (Unity's CurrentAssemblies wrapper is
            // internal there). Newer editors' code-reload analyzer flags it
            // (UAC0005/UAC0006), but only for embedded or local copies of the package.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    found = asm.GetType(fullName, false);
                }
                catch (Exception)
                {
                    found = null; // dynamic / reflection-only assemblies may refuse the lookup
                }
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// "Ns.Type, Asm, Version=…" → "Ns.Type". Splits on the first comma at
        /// bracket depth 0, so generic arguments
        /// (<c>Ns.Wrapper`1[[Ns.Arg, ArgAsm]], Asm</c>) stay intact. Generic
        /// arguments keep their own assembly qualification; only the outer
        /// type's assembly is dropped.
        /// </summary>
        internal static string StripAssemblyName(string typeName)
        {
            int depth = 0;
            for (int i = 0; i < typeName.Length; i++)
            {
                char c = typeName[i];
                if (c == '[') depth++;
                else if (c == ']') depth--;
                else if (c == ',' && depth == 0) return typeName.Substring(0, i).Trim();
            }
            return typeName.Trim();
        }
    }
}
