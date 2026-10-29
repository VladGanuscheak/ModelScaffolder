using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelScaffolder.Core;
using ModelScaffolder.Core.Output;

// modelscaffold <model.json> -o <output-dir> [--templates <dir>] [--framework net10.0] [--dry-run] [--force]
//               [--admin-user <name>] [--admin-password <password> | --admin-password-stdin] [--json]
// Without --admin-user/--admin-password the system user's credentials are asked for interactively
// (prefer that: a password on the command line ends up in shell history).
// --admin-password-stdin reads the password from the first line of standard input (UTF-8); used by the Visual Studio extension.
// --json writes one JSON result to standard output instead of text (machine-readable; used by the Visual Studio extension).
// Exit codes: 0 = ok, 1 = model/generation errors, 2 = bad arguments.

const string Usage =
    "Usage: modelscaffold <model.json> -o <output-dir> [--templates <dir>] [--framework <tfm>] [--dry-run] [--force] " +
    "[--admin-user <name>] [--admin-password <password> | --admin-password-stdin] [--json]";
const int MinPasswordLength = 8;

var options = new ScaffoldOptions();
string? adminUser = null, adminPassword = null;
bool passwordFromStdin = false, json = false;

for (var i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}.");
    try
    {
        switch (args[i])
        {
            case "-o" or "--output": options.OutputDirectory = Next(); break;
            case "-t" or "--templates": options.TemplateDirectory = Next(); break;
            case "--framework": options.Settings.TargetFramework = Next(); break;
            case "--admin-user": adminUser = Next(); break;
            case "--admin-password": adminPassword = Next(); break;
            case "--admin-password-stdin": passwordFromStdin = true; break;
            case "--json": json = true; break;
            case "--dry-run": options.DryRun = true; break;
            case "--force": options.Force = true; break;
            case "-h" or "--help": Console.WriteLine(Usage); return 0;
            default:
                if (args[i].StartsWith('-') || options.ModelPath.Length > 0)
                    throw new ArgumentException($"Unexpected argument '{args[i]}'.");
                options.ModelPath = args[i];
                break;
        }
    }
    catch (ArgumentException ex)
    {
        return Fail(ex.Message);
    }
}

if (options.ModelPath.Length == 0 || options.OutputDirectory.Length == 0)
    return Fail("Model path and output directory are required.");

if (passwordFromStdin)
{
    if (adminPassword is not null)
        return Fail("Use either --admin-password or --admin-password-stdin, not both.");

    // Read raw UTF-8: Console.In would decode with the console code page and change non-ASCII passwords.
    using var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
    adminPassword = stdin.ReadLine() ?? string.Empty;
}

// ---- System user ----
if (options.DryRun && (adminUser is null || adminPassword is null))
{
    adminUser ??= "admin";
    adminPassword ??= "dry-run-placeholder";
}
else
{
    if (adminUser is null || adminPassword is null)
    {
        if (Console.IsInputRedirected)
            return Fail("Input is redirected: pass --admin-user and --admin-password.");

        Console.WriteLine("Default system user (gets every permission of every schema).");
        adminUser ??= ReadLine("  User name [admin]: ") is { Length: > 0 } name ? name : "admin";

        if (adminPassword is null)
        {
            adminPassword = ReadSecret($"  Password (min {MinPasswordLength} characters): ");
            if (ReadSecret("  Repeat password: ") != adminPassword)
                return Fail("Passwords do not match.");
        }
    }

    if (adminUser.Length == 0 || adminUser.Contains(':'))
        return Fail("The user name must be non-empty and must not contain ':'.");
    if (adminPassword.Length < MinPasswordLength)
        return Fail($"The password must have at least {MinPasswordLength} characters.");
}

options.Settings.SystemUserName = adminUser;
options.Settings.SystemUserPasswordHash = HashPassword(adminPassword);

// ---- Generate ----
var result = Scaffolder.Run(options);

if (json)
{
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        succeeded = result.Succeeded,
        dryRun = options.DryRun,
        outputDirectory = Path.GetFullPath(options.OutputDirectory),
        files = result.Files.Select(f => new { action = ActionName(f.Action), path = f.File.RelativePath }),
        diagnostics = result.Diagnostics.Select(d => new
        {
            severity = d.Severity == Severity.Error ? "error" : "warning",
            code = d.Code,
            path = d.Path,
            message = d.Message,
            line = d.Line,
            column = d.Column
        })
    }));
    return result.Succeeded ? 0 : 1;
}

foreach (var file in result.Files)
{
    Console.WriteLine($"{ActionName(file.Action),-10}{file.File.RelativePath}");
}

foreach (var diagnostic in result.Diagnostics)
    (diagnostic.Severity == Severity.Error ? Console.Error : Console.Out).WriteLine(diagnostic);

if (result.Succeeded)
    Console.WriteLine(options.DryRun ? "Dry run: nothing written." : $"Done: {Path.GetFullPath(options.OutputDirectory)}");

return result.Succeeded ? 0 : 1;

// ---- Helpers ----

static string ActionName(FileAction action) => action switch
{
    FileAction.Add => "add",
    FileAction.Update => "update",
    _ => "unchanged"
};

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    Console.Error.WriteLine(Usage);
    return 2;
}

static string ReadLine(string prompt)
{
    Console.Write(prompt);
    return Console.ReadLine()?.Trim() ?? string.Empty;
}

static string ReadSecret(string prompt)
{
    Console.Write(prompt);
    var secret = new StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
            break;
        if (key.Key == ConsoleKey.Backspace)
        {
            if (secret.Length > 0)
                secret.Length--;
            continue;
        }
        if (!char.IsControl(key.KeyChar))
            secret.Append(key.KeyChar);
    }
    Console.WriteLine();
    return secret.ToString();
}

// Same format the generated PasswordHash.Verify expects: "PBKDF2-SHA256$iterations$salt$hash".
static string HashPassword(string password)
{
    const int Iterations = 600_000;
    var salt = RandomNumberGenerator.GetBytes(16);
    var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
    return $"PBKDF2-SHA256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
}
