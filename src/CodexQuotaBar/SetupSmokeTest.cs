using System.IO;
using System.Windows;
using CodexQuotaBar.Core;
namespace CodexQuotaBar;

internal static class SetupSmokeTest
{
    private static AccountInspection Sample(string email = "friend@example.com", string quota = "friend-quota", bool consistent = true)
        => new(new(DateTimeOffset.UtcNow, IdentityBinding.Fingerprint(email), IdentityBinding.Fingerprint(quota), consistent,
            [new("codex", "Codex", [new("primary", 7, 10080, DateTimeOffset.UtcNow.AddDays(1))])]) { SourceProcessExited = true }, email);
    private static async Task CloseAsync(SetupWindow window)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult(); window.Close();
        await closed.Task.WaitAsync(TimeSpan.FromSeconds(4));
    }
    internal static async Task RunAsync(Application app, string reportPath)
    {
        var checks = new List<object>(); var windows = new List<SetupWindow>(); var success = true;
        void Check(string name, bool passed) { checks.Add(new { name, passed }); success &= passed; }
        var folder = Path.ChangeExtension(reportPath, ".work");
        Directory.CreateDirectory(folder);
        var configPath = Path.Combine(folder, "live.local.json");
        if (File.Exists(configPath)) File.Delete(configPath);
        var executable = Environment.ProcessPath!;
        SetupWindow Make(Func<string, CancellationToken, Task<AccountInspection>> read, string? path = null)
        {
            var window = new SetupWindow(path ?? configPath, read, _ => throw new InvalidOperationException("Test must not open login"));
            windows.Add(window); window.ExecutablePath = executable; window.Show(); return window;
        }
        try
        {
            var reads = 0;
            var setup = Make(async (_, token) => { ++reads; await Task.Delay(15, token); return Sample(); });
            Check("unconfigured_cannot_start", !setup.CanStart && !File.Exists(configPath));
            await setup.CheckAsync();
            Check("inspection_does_not_bind_automatically", !File.Exists(configPath) && !setup.CanStart && setup.StatusText.Contains("friend@example.com"));
            setup.DesktopEmail = "other@example.com"; setup.PersonalConfirmed = true;
            Check("wrong_desktop_email_blocks_start", !setup.CanStart);
            setup.DesktopEmail = "friend@example.com"; setup.PersonalConfirmed = false;
            Check("personal_space_ack_required", !setup.CanStart);
            setup.PersonalConfirmed = true;
            Check("matching_manual_confirmation_enables_start", setup.CanStart);
            var configured = new TaskCompletionSource<LiveConfiguration>(TaskCreationOptions.RunContinuationsAsynchronously);
            setup.Configured += value => configured.TrySetResult(value);
            await setup.CommitAsync(); var config = await configured.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var saved = File.ReadAllText(configPath);
            Check("commit_rechecks_and_loads_saved_binding", reads == 2 && config.Identity.Matches(Sample().Snapshot) && LiveConfiguration.Load(configPath) == config);
            Check("saved_config_has_no_email_or_credentials", !saved.Contains("@") && !saved.Contains("access_token") && !saved.Contains("refresh_token"));
            await CloseAsync(setup);
            var original = File.ReadAllText(configPath);
            reads = 0;
            var switched = Make(async (_, token) => { await Task.Delay(15, token); return ++reads == 1 ? Sample() : Sample(quota: "different-workspace"); });
            await switched.CheckAsync(); switched.DesktopEmail = "friend@example.com"; switched.PersonalConfirmed = true;
            var unexpected = false; switched.Configured += _ => unexpected = true;
            await switched.CommitAsync(); await Task.Delay(30);
            Check("account_change_during_commit_is_rejected", !unexpected && !switched.CanStart && switched.StatusText.Contains("发生变化") && File.ReadAllText(configPath) == original);
            await CloseAsync(switched);
            var needsLogin = Make((_, _) => throw new UsageFailureException(UsageState.NeedsLogin));
            await needsLogin.CheckAsync();
            Check("not_logged_in_shows_recovery_without_saving", needsLogin.StatusText.Contains("尚未登录") && !needsLogin.CanStart && File.ReadAllText(configPath) == original);
            await CloseAsync(needsLogin);
            var malformed = Make((_, _) => throw new System.Text.Json.JsonException("PRIVATE_ERROR_TOKEN"));
            await malformed.CheckAsync();
            Check("malformed_response_is_recoverable_and_private", !malformed.CanStart && malformed.QueryStopped && !malformed.StatusText.Contains("PRIVATE_ERROR_TOKEN"));
            await CloseAsync(malformed);
            var mismatch = Make((_, _) => Task.FromResult(Sample(consistent: false)));
            await mismatch.CheckAsync(); mismatch.DesktopEmail = "friend@example.com"; mismatch.PersonalConfirmed = true;
            Check("inconsistent_read_cannot_bind", !mismatch.CanStart);
            await CloseAsync(mismatch);
            var stopped = false;
            var slow = Make(async (_, token) => { try { await Task.Delay(Timeout.Infinite, token); return Sample(); } finally { stopped = true; } });
            var pending = slow.CheckAsync(); await Task.Delay(20); await CloseAsync(slow); await pending;
            Check("closing_cancels_and_waits_for_pending_inspection", stopped && slow.QueryStopped && File.ReadAllText(configPath) == original);
            var blocker = Path.Combine(folder, "not-a-directory"); File.WriteAllText(blocker, "test-only");
            var unwritable = Make((_, _) => Task.FromResult(Sample()), Path.Combine(blocker, "live.local.json"));
            await unwritable.CheckAsync(); unwritable.DesktopEmail = "friend@example.com"; unwritable.PersonalConfirmed = true;
            unexpected = false; unwritable.Configured += _ => unexpected = true;
            await unwritable.CommitAsync(); await Task.Delay(30);
            Check("save_failure_does_not_claim_success", !unexpected && !unwritable.CanStart && unwritable.StatusText.Contains("保存未完成"));
            await CloseAsync(unwritable);
            var badPath = Make((_, _) => Task.FromResult(Sample()));
            badPath.ExecutablePath = Path.Combine(folder, "missing.exe"); await badPath.CheckAsync();
            Check("missing_component_is_recoverable", !badPath.CanStart && badPath.QueryStopped);
            await CloseAsync(badPath);
        }
        catch (Exception error) { success = false; checks.Add(new { name = "unexpected_failure", passed = false, type = error.GetType().Name }); }
        finally
        {
            foreach (var window in windows.Where(w => w.IsLoaded && w.IsVisible).ToArray()) await CloseAsync(window);
            Program.WriteReport(reportPath, new { success, simulated = true, realAuthenticationOpened = false, results = checks });
            app.Shutdown(success ? 0 : 1);
        }
    }
}


