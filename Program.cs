using Mono.Cecil;
using Mono.Cecil.Cil;

static string Sig(MethodDefinition m)
{
    string access = m.IsPublic ? "public" : m.IsFamily ? "protected" : m.IsPrivate ? "private" : m.IsAssembly ? "internal" : "other";
    return $"{access} {m.ReturnType.FullName} {m.FullName}";
}

static IEnumerable<TypeDefinition> Flatten(TypeDefinition t)
{
    yield return t;
    foreach (var n in t.NestedTypes)
        foreach (var x in Flatten(n))
            yield return x;
}

static void DumpType(TypeDefinition t)
{
    Console.WriteLine($"\n=== TYPE {t.FullName} ===");
    Console.WriteLine($"Base: {t.BaseType?.FullName}");
    Console.WriteLine("Interfaces: " + string.Join(", ", t.Interfaces.Select(i => i.InterfaceType.FullName)));
    foreach (var f in t.Fields) Console.WriteLine($"FIELD {(f.IsPublic ? "public" : f.IsFamily ? "protected" : f.IsPrivate ? "private" : "other")} {f.FieldType.FullName} {f.Name}");
    foreach (var p in t.Properties) Console.WriteLine($"PROP {p.PropertyType.FullName} {p.Name} get={p.GetMethod?.Name} set={p.SetMethod?.Name}");
    foreach (var m in t.Methods) Console.WriteLine("METHOD " + Sig(m));
}

var path = args.Length > 0 ? args[0] : "hollowed.dll";
var module = ModuleDefinition.ReadModule(path, new ReaderParameters { ReadSymbols = false });
var all = module.Types.SelectMany(Flatten).ToList();
Console.WriteLine($"Loaded {module.Name}; types={all.Count}");

foreach (var t in all.Where(t => t.FullName.Contains("TunnelVision", StringComparison.OrdinalIgnoreCase) || t.FullName.Contains("ITunnelVision", StringComparison.OrdinalIgnoreCase))) DumpType(t);

var phrase = all.FirstOrDefault(t => t.FullName == "EFT.EPhraseTrigger" || t.Name == "EPhraseTrigger");
Console.WriteLine("\n=== EPhraseTrigger candidates ===");
if (phrase != null)
{
    foreach (var f in phrase.Fields.Where(f => f.IsStatic && (f.Name.Contains("Breath", StringComparison.OrdinalIgnoreCase) || f.Name.Contains("Hurt", StringComparison.OrdinalIgnoreCase) || f.Name.Contains("Death", StringComparison.OrdinalIgnoreCase) || f.Name.Contains("Agony", StringComparison.OrdinalIgnoreCase))))
        Console.WriteLine($"ENUM {f.Name} = {f.Constant}");
}

var playerState = all.FirstOrDefault(t => t.Name == "EPlayerState");
Console.WriteLine("\n=== EPlayerState ===");
if (playerState != null)
    foreach (var f in playerState.Fields.Where(f => f.IsStatic)) Console.WriteLine($"STATE {f.Name} = {f.Constant}");

Console.WriteLine("\n=== Exact prone action methods anywhere ===");
string[] proneMethodNames = { "DoProne", "GoProne", "ToggleProne", "Transit2Prone", "IsNowInProneState" };
foreach (var t in all)
    foreach (var m in t.Methods.Where(m => proneMethodNames.Contains(m.Name) || m.Name.Contains("Prone", StringComparison.OrdinalIgnoreCase) && (m.Name.Contains("Go", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Transit", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Toggle", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Do", StringComparison.OrdinalIgnoreCase))))
        Console.WriteLine($"{t.FullName} :: {Sig(m)}");

Console.WriteLine("\n=== Prone-related movement state types ===");
foreach (var t in all.Where(t => t.FullName.Contains("Prone", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("Crawl", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("Lie", StringComparison.OrdinalIgnoreCase)).Take(160))
    Console.WriteLine($"TYPE {t.FullName} base={t.BaseType?.FullName}");

var movement = all.FirstOrDefault(t => t.FullName == "EFT.MovementContext");
if (movement != null)
{
    Console.WriteLine("\n=== MovementContext prone/state methods ===");
    foreach (var m in movement.Methods.Where(m => m.Name.Contains("Prone", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Pose", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("State", StringComparison.OrdinalIgnoreCase) || m.Name.Contains("Sprint", StringComparison.OrdinalIgnoreCase)))
        Console.WriteLine("METHOD " + Sig(m));
}

var player = all.FirstOrDefault(t => t.FullName == "EFT.Player");
if (player != null)
{
    Console.WriteLine("\n=== Player prone / Say methods ===");
    foreach (var m in player.Methods.Where(m => m.Name.Contains("Prone", StringComparison.OrdinalIgnoreCase) || m.Name == "Say" || m.Name.Contains("Pose", StringComparison.OrdinalIgnoreCase)))
    {
        Console.WriteLine("METHOD " + Sig(m));
        foreach (var p in m.Parameters) Console.WriteLine($"  PARAM {p.Index} {p.ParameterType.FullName} {p.Name} optional={p.IsOptional} default={p.Constant}");
    }
}

var ahc = all.FirstOrDefault(t => t.FullName == "EFT.HealthSystem.ActiveHealthController");
if (ahc != null)
{
    Console.WriteLine("\n=== ActiveHealthController effect APIs ===");
    foreach (var m in ahc.Methods.Where(m => m.Name.Contains("Effect", StringComparison.OrdinalIgnoreCase)))
    {
        if (!(m.Name == "AddEffect" || m.Name == "RemoveEffect" || m.Name == "ResidueEffect" || m.Name.Contains("FindActiveEffect") || m.Name.Contains("FindExistingEffect") || m.Name == "GetEffect" || m.Name == "HasEffect")) continue;
        Console.WriteLine("METHOD " + Sig(m));
        foreach (var gp in m.GenericParameters)
            Console.WriteLine($"  GENERIC {gp.Name} constraints=[{string.Join(",", gp.Constraints.Select(c => c.ConstraintType.FullName))}]");
        foreach (var p in m.Parameters)
            Console.WriteLine($"  PARAM {p.Index} {p.ParameterType.FullName} {p.Name} optional={p.IsOptional} default={p.Constant}");
    }

    var effect = ahc.NestedTypes.FirstOrDefault(t => t.Name == "Effect");
    if (effect != null) DumpType(effect);
}
