using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;

namespace LeonAkasaka.UnionAir.Editor
{
    /// <summary>
    /// Enumerates the build targets a Unity installation knows about and reports which of them
    /// have their platform module installed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="BuildTarget"/> and <see cref="NamedBuildTarget"/> members are read through
    /// reflection rather than named in source. Unity retires platforms by marking the members
    /// <see cref="ObsoleteAttribute"/> instead of removing them, so naming them would produce
    /// deprecation warnings that differ with every Editor version. Reflection also keeps the list
    /// correct on an Editor that ships a platform this package predates.
    /// </para>
    /// <para>
    /// Whether a module is installed is Editor-only knowledge: nothing in the project directory
    /// records which platform modules the Editor that opens it has.
    /// </para>
    /// </remarks>
    internal static class BuildTargetCatalog
    {
        internal readonly struct Entry
        {
            internal Entry(BuildTarget target, BuildTargetGroup group, string namedBuildTarget, bool installed)
            {
                Target = target;
                Group = group;
                NamedBuildTarget = namedBuildTarget;
                Installed = installed;
            }

            internal BuildTarget Target { get; }
            internal BuildTargetGroup Group { get; }

            /// <summary>Named build target for the group, or an empty string when Unity defines none.</summary>
            internal string NamedBuildTarget { get; }

            /// <summary>Whether the Editor can build this target with the modules it has installed.</summary>
            internal bool Installed { get; }
        }

        /// <summary>
        /// Lists every non-obsolete build target, ordered by name.
        /// </summary>
        internal static List<Entry> List()
        {
            var entries = new List<Entry>();
            var seen = new HashSet<int>();

            foreach (var field in typeof(BuildTarget).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (!field.IsLiteral || Attribute.IsDefined(field, typeof(ObsoleteAttribute)))
                    continue;

                BuildTarget target;
                try { target = (BuildTarget)field.GetValue(null); }
                catch { continue; }

                if (target == BuildTarget.NoTarget || !seen.Add((int)target))
                    continue;

                var group = GroupOf(target);
                if (group == BuildTargetGroup.Unknown)
                    continue;

                entries.Add(new Entry(target, group, NamedBuildTargetName(group), IsInstalled(group, target)));
            }

            entries.Sort((a, b) => string.Compare(a.Target.ToString(), b.Target.ToString(), StringComparison.Ordinal));
            return entries;
        }

        /// <summary>
        /// Whether the Editor has the platform module needed to build a target.
        /// </summary>
        internal static bool IsInstalled(BuildTargetGroup group, BuildTarget target)
        {
            try { return BuildPipeline.IsBuildTargetSupported(group, target); }
            catch { return false; }
        }

        /// <summary>
        /// Whether the platform module for the currently active build target is installed.
        /// </summary>
        internal static bool IsActiveTargetInstalled()
        {
            var target = EditorUserBuildSettings.activeBuildTarget;
            return IsInstalled(GroupOf(target), target);
        }

        internal static BuildTargetGroup GroupOf(BuildTarget target)
        {
            try { return BuildPipeline.GetBuildTargetGroup(target); }
            catch { return BuildTargetGroup.Unknown; }
        }

        /// <summary>
        /// Returns the named build target the Editor is currently configured for.
        /// </summary>
        /// <remarks>
        /// The active <see cref="BuildTargetGroup"/> alone does not identify it: a Standalone group
        /// with the Server subtarget selected uses the separate <c>Server</c> named target, and
        /// scripting settings are stored per named target rather than per group.
        /// </remarks>
        internal static NamedBuildTarget Active()
        {
            var group = GroupOf(EditorUserBuildSettings.activeBuildTarget);
            if (group == BuildTargetGroup.Standalone &&
                EditorUserBuildSettings.standaloneBuildSubtarget == StandaloneBuildSubtarget.Server)
                return NamedBuildTarget.Server;

            try { return NamedBuildTarget.FromBuildTargetGroup(group); }
            catch { return NamedBuildTarget.Standalone; }
        }

        private static string NamedBuildTargetName(BuildTargetGroup group)
        {
            try { return NamedBuildTarget.FromBuildTargetGroup(group).TargetName; }
            catch { return ""; }
        }

        /// <summary>
        /// Resolves a caller-supplied named build target such as <c>Standalone</c> or <c>Android</c>.
        /// </summary>
        /// <param name="name">Named build target name; empty resolves to the active one.</param>
        /// <param name="result">Resolved named build target.</param>
        /// <param name="error">Message describing why the name could not be resolved.</param>
        /// <returns><c>true</c> when the name resolved.</returns>
        internal static bool TryResolveNamedBuildTarget(string name, out NamedBuildTarget result, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(name))
            {
                result = Active();
                return true;
            }

            foreach (var candidate in KnownNamedBuildTargets())
            {
                if (string.Equals(candidate.TargetName, name, StringComparison.OrdinalIgnoreCase))
                {
                    result = candidate;
                    return true;
                }
            }

            result = NamedBuildTarget.Standalone;
            error = "Unknown namedBuildTarget '" + name + "'. Known values for this Editor: " +
                    string.Join(", ", KnownNamedBuildTargetNames().ToArray()) + ".";
            return false;
        }

        /// <summary>
        /// Lists the named build targets this Editor defines, ordered by name.
        /// </summary>
        /// <remarks>
        /// Two sources are merged because neither is complete. The public static fields omit console
        /// targets whose named target Unity only derives from the group — <c>GameCoreScarlett</c> is
        /// one — while deriving from groups alone omits <c>Server</c>, which has no build target
        /// group of its own. A name reported by <see cref="List"/> must be resolvable here, or the
        /// two endpoints would disagree about what a client may ask for.
        /// </remarks>
        internal static List<NamedBuildTarget> KnownNamedBuildTargets()
        {
            var targets = new List<NamedBuildTarget>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var field in typeof(NamedBuildTarget).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(NamedBuildTarget) ||
                    Attribute.IsDefined(field, typeof(ObsoleteAttribute)))
                    continue;

                NamedBuildTarget value;
                try { value = (NamedBuildTarget)field.GetValue(null); }
                catch { continue; }

                Add(targets, seen, value);
            }

            foreach (var entry in List())
            {
                try { Add(targets, seen, NamedBuildTarget.FromBuildTargetGroup(entry.Group)); }
                catch { }
            }

            targets.Sort((a, b) => string.Compare(a.TargetName, b.TargetName, StringComparison.Ordinal));
            return targets;
        }

        private static void Add(List<NamedBuildTarget> targets, HashSet<string> seen, NamedBuildTarget value)
        {
            if (string.IsNullOrEmpty(value.TargetName) || !seen.Add(value.TargetName))
                return;
            targets.Add(value);
        }

        internal static List<string> KnownNamedBuildTargetNames()
        {
            var names = new List<string>();
            foreach (var target in KnownNamedBuildTargets())
                names.Add(target.TargetName);
            return names;
        }
    }
}
