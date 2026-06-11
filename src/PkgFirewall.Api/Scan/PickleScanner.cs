using PkgFirewall.Api.Models;

namespace PkgFirewall.Api.Scan;

public record ScanFinding(string Rule, Severity Severity, string Detail);

/// <summary>
/// Scans pickle/.bin model files for dangerous opcodes (the real picklescan check).
/// Pickle can execute arbitrary code on load via GLOBAL/REDUCE/INST/OBJ opcodes
/// referencing os/subprocess/builtins. This reads the opcode stream and flags them.
/// </summary>
public class PickleScanner
{
    // Dangerous global references commonly used in weaponised pickles.
    private static readonly string[] DangerModules =
        { "os", "subprocess", "sys", "builtins", "posix", "nt", "socket", "shutil", "runpy", "pty" };
    private static readonly string[] DangerCallables =
        { "system", "popen", "exec", "eval", "spawn", "call", "check_output", "fork", "load", "loads" };

    // pickle opcodes that can trigger execution
    private const byte GLOBAL = (byte)'c';
    private const byte STACK_GLOBAL = 0x93;
    private const byte REDUCE = (byte)'R';
    private const byte INST = (byte)'i';
    private const byte OBJ = (byte)'o';
    private const byte BUILD = (byte)'b';

    public IReadOnlyList<ScanFinding> ScanBytes(ReadOnlySpan<byte> data)
    {
        var findings = new List<ScanFinding>();
        bool reduceSeen = false;

        for (int i = 0; i < data.Length; i++)
        {
            var op = data[i];
            if (op is REDUCE or BUILD) reduceSeen = true;

            if (op == GLOBAL)
            {
                // GLOBAL: 'c' module '\n' name '\n'
                var module = ReadLine(data, ref i);
                var name = ReadLine(data, ref i);
                FlagGlobal(findings, module, name);
            }
            else if (op is STACK_GLOBAL or INST or OBJ)
            {
                findings.Add(new ScanFinding("PICKLE_DYNAMIC_GLOBAL", Severity.High,
                    $"opcode 0x{op:X2} performs dynamic global/object construction"));
            }
        }

        if (reduceSeen && findings.Count == 0)
            findings.Add(new ScanFinding("PICKLE_REDUCE", Severity.Medium,
                "REDUCE/BUILD opcode present — verify the callable is safe"));

        return findings;
    }

    private static void FlagGlobal(List<ScanFinding> f, string module, string name)
    {
        var modHit = DangerModules.Any(m => module.Equals(m, StringComparison.OrdinalIgnoreCase));
        var callHit = DangerCallables.Any(c => name.Contains(c, StringComparison.OrdinalIgnoreCase));
        if (modHit || callHit)
            f.Add(new ScanFinding("PICKLE_DANGEROUS_IMPORT", Severity.Critical,
                $"references {module}.{name}"));
    }

    private static string ReadLine(ReadOnlySpan<byte> data, ref int i)
    {
        var start = ++i;
        while (i < data.Length && data[i] != (byte)'\n') i++;
        return System.Text.Encoding.ASCII.GetString(data.Slice(start, Math.Max(0, i - start)));
    }
}
