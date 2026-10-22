// Author: 0Shard
// Description: Session-owned, optional RJW reflection bindings and lossless core anatomy conversion.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace Eternal.Compatibility
{
    internal sealed class RjwReflectionCache
    {
        internal static readonly string[] HediffFields =
        {
            "ageTicks", "tickAdded", "visible", "severityInt", "recordedTale", "causesNoPain",
            "combatLogText", "canBeThreateningToPart", "sourceDef", "sourceHediffDef",
            "sourceBodyPartGroup", "sourceLabel", "sourceToolLabel"
        };
        private static readonly Dictionary<string, Dictionary<string, Type>> CompSchema = new Dictionary<string, Dictionary<string, Type>>
        {
            ["rjw.HediffComp_SexPart"] = new Dictionary<string, Type>
            {
                ["baseSize"] = typeof(float),
                ["originalOwnerSize"] = typeof(float),
                ["originalOwnerRace"] = typeof(string),
                ["previousOwner"] = typeof(string),
                ["fluidOverride"] = typeof(Def),
                ["partFluidMultiplier"] = typeof(float),
                ["isTransplant"] = typeof(bool),
                ["forcedSize"] = typeof(float?),
                ["discovered"] = typeof(bool),
                ["initialised"] = typeof(bool)
            },
            ["rjw.HediffComp_GrowsWithOwner"] = new Dictionary<string, Type> { ["lastOwnerSize"] = typeof(float) },
            ["rjw.HediffComp_Ovipositor"] = new Dictionary<string, Type>
            {
                ["nextEggTick"] = typeof(int),
                ["eggInterval"] = typeof(IntRange)
            }
        };

        private readonly Dictionary<Type, Dictionary<string, FieldInfo>> fields = new Dictionary<Type, Dictionary<string, FieldInfo>>();
        private readonly Dictionary<string, FieldInfo> slotDefs = new Dictionary<string, FieldInfo>();
        private bool initialized;
        private Type naturalType;
        private Type artificialType;
        private FieldInfo abilities;
        private MethodInfo getPawnData;
        private readonly List<FieldInfo> pawnCacheFields = new List<FieldInfo>();
        private MethodInfo getRaceGroup;
        private MethodInfo chooseRacePart;
        private MethodInfo getHediffDef;
        private MethodInfo makeRacePart;
        private PropertyInfo racePartIsNone;
        private readonly Dictionary<string, MethodInfo> legacyAdders = new Dictionary<string, MethodInfo>();

        internal bool IsAvailable { get; private set; }

        internal void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            naturalType = AccessTools.TypeByName("rjw.Hediff_NaturalSexPart");
            if (naturalType == null)
            {
                return;
            }

            try
            {
                var version = naturalType.Assembly.GetName().Version;
                if (version == null || version.Major != 1 || version.Minor != 6)
                {
                    throw new InvalidOperationException("Unsupported RJW assembly branch " + version);
                }

                ValidateInstanceFields(naturalType, Array.Empty<string>());
                ValidateInstanceFields(RequiredType("rjw.HediffWithCompDesc"), Array.Empty<string>());
                ValidateInstanceFields(RequiredType("rjw.HediffCompDesc"), Array.Empty<string>());
                artificialType = RequiredType("rjw.Hediff_ArtificialSexPart");
                CacheFields(typeof(Hediff), HediffFields);
                abilities = RequireField(typeof(Hediff), "abilities");
                foreach (var schema in CompSchema)
                {
                    var type = RequiredType(schema.Key);
                    CacheFields(type, schema.Value.Keys);
                    foreach (var expected in schema.Value)
                    {
                        var actual = fields[type][expected.Key].FieldType;
                        if (expected.Value == typeof(Def) ? !typeof(Def).IsAssignableFrom(actual) : actual != expected.Value)
                        {
                            throw new InvalidOperationException(expected.Value == typeof(Def)
                                ? "fluidOverride no longer holds a Def" : type.FullName + "." + expected.Key + " type changed");
                        }
                    }
                    // _eggTip is the only unsaved instance cache in the pinned core comp.
                    var known = schema.Key == "rjw.HediffComp_SexPart" ? schema.Value.Keys.Concat(new[] { "_eggTip" }) : schema.Value.Keys;
                    ValidateInstanceFields(type, known);
                }

                var data = RequiredType("rjw.PawnData");
                getPawnData = RequireMethod(RequiredType("rjw.PawnExtensions"), "GetRJWPawnData", typeof(Pawn));
                foreach (var name in new[] { "genitals", "breasts", "anus" })
                {
                    var field = RequireField(data, name);
                    if (field.FieldType != typeof(List<Hediff>))
                    {
                        throw new InvalidOperationException(name + " cache type changed");
                    }

                    pawnCacheFields.Add(field);
                }
                var xxx = RequiredType("rjw.xxx");
                foreach (var name in new[] { "genitalsDef", "mechgenitalsDef", "breastsDef", "mechbreastsDef", "anusDef", "mechanusDef" })
                {
                    slotDefs.Add(name, RequireField(xxx, name));
                }

                var group = RequiredType("rjw.RaceGroupDef");
                var racePart = RequiredType("rjw.RacePartDef");
                getRaceGroup = RequireMethod(RequiredType("rjw.RaceGroupDef_Helper"), "TryGetRaceGroupDef", typeof(Pawn), group.MakeByRefType());
                chooseRacePart = RequireMethod(RequiredType("rjw.RacePartDef_Helper"), "TryChooseRacePartDef", typeof(Pawn), group, typeof(BodyPartRecord), racePart.MakeByRefType(), typeof(Gender));
                getHediffDef = RequireMethod(racePart, "TryGetHediffDef", typeof(HediffDef).MakeByRefType());
                makeRacePart = RequireMethod(RequiredType("rjw.RacePartDef_Helper"), "MakePart", typeof(HediffDef), typeof(Pawn), typeof(BodyPartRecord), racePart);
                racePartIsNone = AccessTools.Property(racePart, "IsNone") ?? throw new MissingMemberException(racePart.FullName, "IsNone");
                var legacy = RequiredType("rjw.LegacySexPartAdder");
                var selector = RequireMethod(legacy, "GenderTechLevelCheck", typeof(Pawn));
                if (selector.ReturnType != typeof(double))
                {
                    throw new InvalidOperationException("Legacy natural selector return type changed");
                }

                legacyAdders.Add("genitals", RequireMethod(legacy, "AddGenitals", typeof(Pawn), typeof(Pawn), typeof(Gender), typeof(BodyPartRecord), typeof(HediffDef)));
                legacyAdders.Add("breasts", RequireMethod(legacy, "AddBreasts", typeof(Pawn), typeof(BodyPartRecord), typeof(Pawn)));
                legacyAdders.Add("anus", RequireMethod(legacy, "AddAnus", typeof(Pawn), typeof(BodyPartRecord), typeof(Pawn)));
                IsAvailable = true;
            }
            catch (Exception ex)
            {
                Log.Warning("[Eternal] RJW anatomy API is incompatible (" + naturalType.Assembly.GetName().Version + "): " + ex.Message + ". Saved anatomy remains pending.");
            }
        }

        internal bool IsNatural(Hediff hediff) => hediff != null && naturalType != null && naturalType.IsInstanceOfType(hediff);
        internal bool IsArtificial(Hediff hediff) => hediff != null && artificialType != null && artificialType.IsInstanceOfType(hediff);
        internal bool IsNaturalDef(HediffDef def) => def?.hediffClass != null && naturalType != null && naturalType.IsAssignableFrom(def.hediffClass);

        internal bool SupportsSnapshotDef(HediffDef def) => def != null && naturalType != null && def.hediffClass == naturalType &&
            (def.abilities == null || def.abilities.Count == 0) && def.comps != null &&
            def.comps.All(c => c.compClass != null && CompSchema.ContainsKey(c.compClass.FullName)) &&
            def.comps.Any(c => c.compClass.FullName == "rjw.HediffComp_SexPart");

        internal string SlotKind(BodyPartRecord part)
        {
            if (part == null)
            {
                return null;
            }

            foreach (var entry in slotDefs)
            {
                if (ReferenceEquals(entry.Value.GetValue(null), part.def))
                {
                    if (entry.Key.Contains("genitals"))
                    {
                        return "genitals";
                    }

                    if (entry.Key.Contains("breasts"))
                    {
                        return "breasts";
                    }

                    return "anus";
                }
            }

            return null;
        }

        internal RjwAnatomySnapshot Capture(Hediff hediff)
        {
            if (hediff.GetType() != naturalType)
            {
                throw new InvalidOperationException("Unsupported anatomy class " + hediff.GetType().FullName);
            }

            if (abilities.GetValue(hediff) is IList list && list.Count != 0)
            {
                throw new InvalidOperationException("Anatomy with saved abilities requires an additional adapter");
            }

            var snapshot = new RjwAnatomySnapshot
            {
                HediffDefName = hediff.def.defName,
                HediffClassName = hediff.GetType().FullName,
                OriginalLoadId = hediff.loadID,
                AttachedLoadId = hediff.loadID,
                CombatLogEntry = hediff.combatLogEntry,
                Fields = CaptureFields(hediff, typeof(Hediff))
            };
            foreach (var comp in ((HediffWithComps)hediff).comps)
            {
                var type = comp.GetType();
                if (!CompSchema.ContainsKey(type.FullName))
                {
                    throw new InvalidOperationException("Unsupported anatomy component " + type.FullName);
                }

                snapshot.Comps.Add(new RjwSnapshotComp { TypeName = type.FullName, Fields = CaptureFields(comp, type) });
            }
            if (!snapshot.Comps.Any(c => c.TypeName == "rjw.HediffComp_SexPart"))
            {
                throw new InvalidOperationException("Anatomy lacks its size/owner component");
            }

            return snapshot;
        }

        internal Hediff MakeSnapshot(Pawn pawn, BodyPartRecord part, RjwAnatomySnapshot snapshot)
        {
            var def = DefDatabase<HediffDef>.GetNamedSilentFail(snapshot.HediffDefName);
            if (!SupportsSnapshotDef(def) || snapshot.HediffClassName != naturalType.FullName)
            {
                throw new InvalidOperationException("Saved anatomy definition/class cannot be resolved: " + snapshot.HediffDefName);
            }

            var hediff = HediffMaker.MakeHediff(def, pawn, part);
            ApplySnapshot(hediff, snapshot, true);
            return hediff;
        }

        internal void ApplySnapshot(Hediff hediff, RjwAnatomySnapshot snapshot, bool beforeAdd)
        {
            ApplyFields(hediff, typeof(Hediff), snapshot.Fields);
            var comps = ((HediffWithComps)hediff).comps;
            if (snapshot.Comps == null || comps.Count != snapshot.Comps.Count)
            {
                throw new InvalidOperationException("Saved anatomy component cohort changed");
            }

            for (int i = 0; i < comps.Count; i++)
            {
                if (comps[i].GetType().FullName != snapshot.Comps[i].TypeName || !CompSchema.ContainsKey(snapshot.Comps[i].TypeName))
                {
                    throw new InvalidOperationException("Saved anatomy component schema changed");
                }

                ApplyFields(comps[i], comps[i].GetType(), snapshot.Comps[i].Fields);
                // PostAdd must not reroll an established part, even if its saved initializer flag was false.
                if (beforeAdd && snapshot.Comps[i].TypeName == "rjw.HediffComp_SexPart")
                {
                    fields[comps[i].GetType()]["initialised"].SetValue(comps[i], true);
                }
            }
            hediff.combatLogEntry = snapshot.CombatLogEntry;
        }

        internal void RefreshCaches(Pawn pawn)
        {
            var data = getPawnData.Invoke(null, new object[] { pawn });
            if (data == null)
            {
                return;
            }

            foreach (var field in pawnCacheFields)
            {
                field.SetValue(data, new List<Hediff>());
            }
        }

        // A selected None is success. A selected but incompatible definition is a pending failure, never a legacy reroll.
        internal bool TryCreateRaceFallback(Pawn pawn, BodyPartRecord part, out Hediff hediff)
        {
            hediff = null;
            var groupArgs = new object[] { pawn, null };
            if (!(bool)getRaceGroup.Invoke(null, groupArgs))
            {
                return false;
            }

            var chooseArgs = new object[] { pawn, groupArgs[1], part, null, pawn.gender };
            if (!(bool)chooseRacePart.Invoke(null, chooseArgs))
            {
                return false;
            }

            var racePart = chooseArgs[3] ?? throw new InvalidOperationException("Race part selection returned null");
            if ((bool)racePartIsNone.GetValue(racePart))
            {
                return true;
            }

            var defArgs = new object[] { null };
            if (!(bool)getHediffDef.Invoke(racePart, defArgs) || !IsNaturalDef(defArgs[0] as HediffDef))
            {
                throw new InvalidOperationException("Race fallback did not select a resolvable natural anatomy definition");
            }

            hediff = makeRacePart.Invoke(null, new[] { defArgs[0], pawn, part, racePart }) as Hediff;
            if (!IsNatural(hediff) || hediff.Part != part)
            {
                throw new InvalidOperationException("Race fallback returned incompatible anatomy");
            }

            return true;
        }

        internal void AddLegacyFallback(Pawn pawn, BodyPartRecord part, string kind)
        {
            var args = kind == "genitals" ? new object[] { pawn, null, pawn.gender, part, null } : new object[] { pawn, part, null };
            legacyAdders[kind].Invoke(null, args);
        }

        private List<RjwSnapshotValue> CaptureFields(object target, Type type)
        {
            return fields[type].Select(entry => CaptureValue(entry.Value, target)).ToList();
        }

        private static RjwSnapshotValue CaptureValue(FieldInfo field, object target)
        {
            var value = field.GetValue(target);
            var saved = value switch
            {
                null => new RjwSnapshotValue(),
                float number => new RjwSnapshotValue { Number = number },
                int integer => new RjwSnapshotValue { Integer = integer },
                bool boolean => new RjwSnapshotValue { Boolean = boolean },
                string text => new RjwSnapshotValue { Text = text },
                Def def => new RjwSnapshotValue { Text = def.defName },
                IntRange range => new RjwSnapshotValue { RangeMin = range.min, RangeMax = range.max },
                _ => throw new InvalidOperationException("Unsupported saved field " + field.Name)
            };
            saved.Name = field.Name;
            saved.TypeName = field.FieldType.FullName;
            saved.IsNull = value == null;
            return saved;
        }

        private void ApplyFields(object target, Type type, List<RjwSnapshotValue> values)
        {
            var schema = fields[type];
            if (values == null || values.Count != schema.Count || values.Select(v => v.Name).Distinct().Count() != schema.Count)
            {
                throw new InvalidOperationException("Saved field schema is incomplete");
            }

            foreach (var saved in values)
            {
                if (!schema.TryGetValue(saved.Name, out var field) || field.FieldType.FullName != saved.TypeName)
                {
                    throw new InvalidOperationException("Saved field type changed: " + saved.Name);
                }

                object value = null;
                var fieldType = Nullable.GetUnderlyingType(field.FieldType) ?? field.FieldType;
                if (!saved.IsNull)
                {
                    value = fieldType switch
                    {
                        var t when t == typeof(float) => saved.Number,
                        var t when t == typeof(int) => saved.Integer,
                        var t when t == typeof(bool) => saved.Boolean,
                        var t when t == typeof(string) => saved.Text,
                        var t when t == typeof(IntRange) => new IntRange(saved.RangeMin, saved.RangeMax),
                        var t when typeof(Def).IsAssignableFrom(t) => GenDefDatabase.GetDef(t, saved.Text, false)
                            ?? throw new InvalidOperationException("Saved definition is absent: " + saved.Text),
                        _ => throw new InvalidOperationException("Unsupported saved field type: " + fieldType.FullName)
                    };
                }
                else if (field.FieldType.IsValueType && Nullable.GetUnderlyingType(field.FieldType) == null)
                {
                    throw new InvalidOperationException("Saved nonnullable field is null: " + saved.Name);
                }

                field.SetValue(target, value);
            }
        }

        private void CacheFields(Type type, IEnumerable<string> names)
        {
            fields.Add(type, names.ToDictionary(name => name, name => RequireField(type, name)));
        }
        private static Type RequiredType(string name) => AccessTools.TypeByName(name) ?? throw new TypeLoadException(name);
        private static void ValidateInstanceFields(Type type, IEnumerable<string> known)
        {
            var allowed = new HashSet<string>(known);
            var unexpected = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(field => !allowed.Contains(field.Name)).Select(field => field.Name).ToList();
            if (unexpected.Count != 0)
            {
                throw new InvalidOperationException(type.FullName + " has unsupported instance state: " + string.Join(", ", unexpected));
            }
        }
        private static FieldInfo RequireField(Type type, string name) => AccessTools.Field(type, name) ?? throw new MissingFieldException(type.FullName, name);
        private static MethodInfo RequireMethod(Type type, string name, params Type[] parameters) =>
            AccessTools.Method(type, name, parameters) ?? throw new MissingMethodException(type.FullName, name);
    }
}
