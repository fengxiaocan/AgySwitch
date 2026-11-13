using System.IO;
using System.Text;
using AgySwitch.Models;
using AgySwitch.Services;

static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static CredentialSnapshot Token(string email, string version) => new(Encoding.UTF8.GetBytes($"{{\"token\":{{\"access_token\":\"{version}\"}},\"id_token_email\":\"{email}\"}}"), "antigravity", null, 2);
var root = Path.Combine(Path.GetTempPath(), "AgySwitch-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var store = new FakeStore();
    var paths = new AppPaths(root, root);
    var accounts = new AccountService(new AppSettings { GeminiDirectory = root }, new AccountIdentityReader(), new AppLogger(paths), store, root);
    var switching = new SwitchService(accounts, new CliProcessService(), new AppLogger(paths));
    store.Write(WindowsCredentialStore.CurrentTarget, Token("one@example.com", "old"));
    var one = accounts.SaveCurrent("one");
    store.Write(WindowsCredentialStore.CurrentTarget, Token("two@example.com", "two"));
    var two = accounts.SaveCurrent("two");
    Check(accounts.GetProfiles().Single(p => p.Id == two.Id).IsCurrent, "current identity");
    store.Write(WindowsCredentialStore.CurrentTarget, Token("two@example.com", "refreshed"));
    switching.SwitchTo(one, true);
    Check(accounts.GetCurrentIdentity()?.Email == "one@example.com", "switch to one");
    switching.SwitchTo(two, true);
    Check(Encoding.UTF8.GetString(store.Read(WindowsCredentialStore.CurrentTarget)!.Blob).Contains("refreshed"), "refreshed token saved");
    store.FailNextCurrentWrite = true;
    try { switching.SwitchTo(one, true); throw new Exception("switch should fail"); } catch (IOException) { }
    Check(accounts.GetCurrentIdentity()?.Email == "two@example.com", "rollback");
    var profileFiles = Directory.GetFiles(paths.GetProfileDirectory(one.Id));
    Check(profileFiles.Length == 1 && Path.GetFileName(profileFiles[0]) == "profile.json", "no plaintext token");
    store.Write(WindowsCredentialStore.CurrentTarget, new CredentialSnapshot(Encoding.UTF8.GetBytes("{}"), "antigravity", null, 2));
    try { switching.SwitchTo(one, true); throw new Exception("unidentified current credential accepted"); } catch (InvalidOperationException) { }
    store.Write(WindowsCredentialStore.CurrentTarget, Token("two@example.com", "two"));
    store.Write(WindowsCredentialStore.ProfileTarget(paths, one.Id), Token("wrong@example.com", "wrong"));
    try { switching.SwitchTo(one, true); throw new Exception("mismatched target accepted"); } catch (InvalidOperationException) { }
    Check(accounts.GetCurrentIdentity()?.Email == "two@example.com", "invalid target changed current");
    store.Write(WindowsCredentialStore.ProfileTarget(paths, one.Id), Token("one@example.com", "one"));
    store.Delete(WindowsCredentialStore.CurrentTarget);
    switching.SwitchTo(one, true);
    Check(accounts.GetCurrentIdentity()?.Email == "one@example.com", "switch when logged out");
    Check(store.Read(WindowsCredentialStore.RecoveryTarget(paths)) is null, "stale recovery retained");
    var native = new WindowsCredentialStore();
    var testTarget = "AgySwitch:smoketest:" + Guid.NewGuid().ToString("N");
    try
    {
        var sample = Token("test@example.com", "sample");
        native.Write(testTarget, sample);
        var actual = native.Read(testTarget);
        Check(actual != null && actual.Blob.SequenceEqual(sample.Blob) && actual.UserName == sample.UserName && actual.Persist == sample.Persist, "native round trip");
    }
    finally { native.Delete(testTarget); }
    Console.WriteLine("Credential round trip, account backup, refreshed token, switching, rollback and unknown identity: passed.");
}
finally { Directory.Delete(root, true); }

sealed class FakeStore : ICredentialStore
{
    private readonly Dictionary<string, CredentialSnapshot> values = new();
    public bool FailNextCurrentWrite { get; set; }
    public CredentialSnapshot? Read(string target) => values.GetValueOrDefault(target);
    public void Write(string target, CredentialSnapshot value)
    {
        if (target == WindowsCredentialStore.CurrentTarget && FailNextCurrentWrite) { FailNextCurrentWrite = false; throw new IOException("injected failure"); }
        values[target] = value;
    }
    public void Delete(string target) => values.Remove(target);
}
