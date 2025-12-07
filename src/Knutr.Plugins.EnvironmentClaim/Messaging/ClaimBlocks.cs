namespace Knutr.Plugins.EnvironmentClaim.Messaging;

/// <summary>
/// Helper for building Slack Block Kit messages for environment claims.
/// </summary>
internal static class ClaimBlocks
{
    public static object Section(string text) => new
    {
        type = "section",
        text = new { type = "mrkdwn", text }
    };

    public static object Context(params string[] elements) => new
    {
        type = "context",
        elements = elements.Select(e => new { type = "mrkdwn", text = e }).ToArray()
    };

    public static object Divider() => new { type = "divider" };

    public static object Header(string text) => new
    {
        type = "header",
        text = new { type = "plain_text", text, emoji = true }
    };

    public static object Actions(string blockId, params object[] elements) => new
    {
        type = "actions",
        block_id = blockId,
        elements
    };

    public static object Button(string text, string actionId, string value, string? style = "primary")
    {
        // Slack only accepts "primary" or "danger" - omit style for neutral buttons
        if (style == "primary" || style == "danger")
        {
            return new
            {
                type = "button",
                text = new { type = "plain_text", text, emoji = true },
                action_id = actionId,
                value,
                style
            };
        }

        // No style - neutral button
        return new
        {
            type = "button",
            text = new { type = "plain_text", text, emoji = true },
            action_id = actionId,
            value
        };
    }

    public static object DangerButton(string text, string actionId, string value) =>
        Button(text, actionId, value, "danger");

    public static object NeutralButton(string text, string actionId, string value) =>
        Button(text, actionId, value, style: null);

    /// <summary>
    /// Builds a claim result message with Block Kit.
    /// </summary>
    public static (string Text, object[] Blocks) ClaimSuccess(string environment, string? note)
    {
        var text = $"Claimed {environment}";
        var blocks = new List<object>
        {
            Section($":lock:  *Environment Claimed*"),
            Section($"You now have exclusive access to `{environment}`.")
        };

        if (!string.IsNullOrEmpty(note))
        {
            blocks.Add(Section($"_{note}_"));
        }

        blocks.Add(Context($"Use `/knutr release {environment}` when done"));

        return (text, blocks.ToArray());
    }

    public static (string Text, object[] Blocks) ClaimAlreadyOwned(string environment)
    {
        var text = $"Already own {environment}";
        return (text, new object[]
        {
            Section($":information_source:  *Already Claimed*"),
            Section($"You already have `{environment}` claimed."),
            Context($"Use `/knutr release {environment}` to release")
        });
    }

    public static (string Text, object[] Blocks) ClaimBlocked(string environment, string ownerId)
    {
        var text = $"Cannot claim {environment}";
        return (text, new object[]
        {
            Section($":no_entry:  *Environment Unavailable*"),
            Section($"`{environment}` is claimed by <@{ownerId}>."),
            Context(
                $"`/knutr nudge {environment}` to ask them",
                $"`/knutr mutiny {environment}` to force takeover"
            )
        });
    }

    public static (string Text, object[] Blocks) ReleaseSuccess(string environment)
    {
        return ($"Released {environment}", new object[]
        {
            Section($":unlock:  *Environment Released*"),
            Section($"`{environment}` is now available for others.")
        });
    }

    public static (string Text, object[] Blocks) ReleaseNotClaimed(string environment)
    {
        return ($"{environment} not claimed", new object[]
        {
            Section($":information_source:  `{environment}` is not currently claimed.")
        });
    }

    public static (string Text, object[] Blocks) ReleaseNotOwner(string environment, string ownerId)
    {
        return ($"Cannot release {environment}", new object[]
        {
            Section($":no_entry:  *Cannot Release*"),
            Section($"`{environment}` is claimed by <@{ownerId}>."),
            Context($"Use `/knutr mutiny {environment}` to force takeover")
        });
    }

    public static (string Text, object[] Blocks) ClaimsList(IReadOnlyList<EnvironmentClaim> claims)
    {
        if (claims.Count == 0)
        {
            return ("No claims", new object[]
            {
                Section($":white_check_mark:  *No Claimed Environments*"),
                Section("All environments are available.")
            });
        }

        var text = $"{claims.Count} environments claimed";
        var blocks = new List<object>
        {
            Section($":clipboard:  *Claimed Environments*  ({claims.Count})")
        };

        foreach (var claim in claims)
        {
            var duration = FormatDuration(claim.ClaimDuration);
            var status = claim.Status != ClaimStatus.Claimed ? $" • _{claim.Status}_" : "";
            var line = $"`{claim.Environment}`  <@{claim.UserId}>  •  {duration}{status}";

            if (!string.IsNullOrEmpty(claim.Note))
            {
                line += $"\n      _{claim.Note}_";
            }

            blocks.Add(Section(line));
        }

        return (text, blocks.ToArray());
    }

    public static (string Text, object[] Blocks) MyClaimsList(IReadOnlyList<EnvironmentClaim> claims)
    {
        if (claims.Count == 0)
        {
            return ("No claims", new object[]
            {
                Section($":information_source:  You don't have any environments claimed.")
            });
        }

        var text = $"You have {claims.Count} environments claimed";
        var blocks = new List<object>
        {
            Section($":bust_in_silhouette:  *Your Claims*  ({claims.Count})")
        };

        foreach (var claim in claims)
        {
            var duration = FormatDuration(claim.ClaimDuration);
            var line = $"`{claim.Environment}`  •  {duration}";

            if (!string.IsNullOrEmpty(claim.Note))
            {
                line += $"  •  _{claim.Note}_";
            }

            blocks.Add(Section(line));
        }

        blocks.Add(Context("Use `/knutr release [env]` to release"));

        return (text, blocks.ToArray());
    }

    // ─────────────────────────────────────────────────────────────────
    // Nudge Messages
    // ─────────────────────────────────────────────────────────────────

    /// <summary>Ephemeral message shown to the requester when they nudge.</summary>
    public static (string Text, object[] Blocks) NudgeStartedEphemeral(string environment, string ownerId)
    {
        return ($"Nudged {ownerId} about {environment}", new object[]
        {
            Section($":wave:  I've nudged <@{ownerId}> to ask if they're done with `{environment}`.")
        });
    }

    /// <summary>DM sent to the claim owner asking if they want to release.</summary>
    /// <param name="yesActionId">Action ID for yes button (should include workflow ID)</param>
    /// <param name="noActionId">Action ID for no button (should include workflow ID)</param>
    public static (string Text, object[] Blocks) NudgeDmToOwner(
        string environment, string requesterId, TimeSpan claimDuration, string? note,
        string yesActionId, string noActionId)
    {
        var duration = FormatDuration(claimDuration);
        var blocks = new List<object>
        {
            Section($":wave:  <@{requesterId}> is asking if you're done with `{environment}`."),
            Context($"You've had this claimed for {duration}" + (note != null ? $" • _{note}_" : "")),
            Divider(),
            Section("Would you like to release it?"),
            Actions("nudge_response",
                Button("Yes, release it", yesActionId, environment),
                NeutralButton("No, I'm still using it", noActionId, environment))
        };

        return ($"Release request for {environment}", blocks.ToArray());
    }

    /// <summary>Update to owner's DM after they respond.</summary>
    public static (string Text, object[] Blocks) NudgeOwnerReleased(string environment, string requesterId)
    {
        return ($"Released {environment}", new object[]
        {
            Section($":white_check_mark:  You released `{environment}`."),
            Context($"<@{requesterId}> has been notified")
        });
    }

    public static (string Text, object[] Blocks) NudgeOwnerKept(string environment, string requesterId)
    {
        return ($"Kept {environment}", new object[]
        {
            Section($":lock:  You're keeping `{environment}`."),
            Context($"<@{requesterId}> has been notified")
        });
    }

    /// <summary>DM to requester when owner releases.</summary>
    public static (string Text, object[] Blocks) NudgeSuccessDmToRequester(string environment, string ownerId)
    {
        return ($"{environment} released!", new object[]
        {
            Section($":tada:  <@{ownerId}> released `{environment}`!"),
            Context($"Use `/knutr claim {environment}` to claim it")
        });
    }

    /// <summary>DM to requester when owner declines.</summary>
    public static (string Text, object[] Blocks) NudgeDeclinedDmToRequester(string environment, string ownerId)
    {
        return ($"{ownerId} is still using {environment}", new object[]
        {
            Section($":no_entry:  <@{ownerId}> is still using `{environment}`."),
            Context($"Try again later or use `/knutr mutiny {environment}` to force takeover")
        });
    }

    /// <summary>DM to requester when owner doesn't respond.</summary>
    public static (string Text, object[] Blocks) NudgeTimeoutDmToRequester(string environment, string ownerId)
    {
        return ($"No response from {ownerId}", new object[]
        {
            Section($":clock3:  <@{ownerId}> didn't respond about `{environment}`."),
            Context($"Use `/knutr mutiny {environment}` to force takeover")
        });
    }

    public static (string Text, object[] Blocks) NudgeStarted(string environment, string ownerId)
    {
        return ($"Nudging owner of {environment}", new object[]
        {
            Section($":wave:  *Nudge Sent*"),
            Section($"Asked <@{ownerId}> if they're done with `{environment}`."),
            Context("They'll be prompted to release or keep the claim")
        });
    }

    public static (string Text, object[] Blocks) NudgeNotClaimed(string environment)
    {
        return ($"{environment} not claimed", new object[]
        {
            Section($":information_source:  `{environment}` is not currently claimed."),
            Context($"Use `/knutr claim {environment}` to claim it")
        });
    }

    public static (string Text, object[] Blocks) NudgeOwnClaim(string environment)
    {
        return ($"You own {environment}", new object[]
        {
            Section($":thinking_face:  You own `{environment}`."),
            Context($"Did you mean `/knutr release {environment}`?")
        });
    }

    // ─────────────────────────────────────────────────────────────────
    // Mutiny Messages (Subtle pirate theme)
    // ─────────────────────────────────────────────────────────────────

    /// <summary>Ephemeral message when starting mutiny.</summary>
    public static (string Text, object[] Blocks) MutinyStartedEphemeral(string environment, string ownerId)
    {
        return ($"Taking over {environment}", new object[]
        {
            Section($":crossed_swords:  Initiating takeover of `{environment}` from <@{ownerId}>..."),
            Context("Check your DMs to confirm")
        });
    }

    /// <summary>DM message asking for confirmation with buttons.</summary>
    public static (string Text, object[] Blocks) MutinyConfirmPrompt(
        string environment, string ownerId, string yesActionId, string noActionId)
    {
        return ($"Confirm takeover for {environment}?", new object[]
        {
            Section($":crossed_swords:  *Take over `{environment}`?*\n\nThis will remove <@{ownerId}>'s claim and give you control."),
            Actions("mutiny_confirm",
                DangerButton("Yes, take over", yesActionId, environment),
                NeutralButton("Cancel", noActionId, environment))
        });
    }

    /// <summary>Updated DM after confirmation.</summary>
    public static (string Text, object[] Blocks) MutinyConfirmed(string environment)
    {
        return ($"Takeover complete!", new object[]
        {
            Section($":ship:  *You now control `{environment}`!*")
        });
    }

    public static (string Text, object[] Blocks) MutinyCancelled()
    {
        return ("Takeover cancelled", new object[]
        {
            Section(":x:  Takeover cancelled.")
        });
    }

    /// <summary>DM to the user who lost their claim.</summary>
    public static (string Text, object[] Blocks) MutinyVictimDm(string environment, string mutineerId)
    {
        return ($"Your claim on {environment} was taken over", new object[]
        {
            Section($":crossed_swords:  <@{mutineerId}> has taken over `{environment}`."),
            Context("Reach out to them if you still need the environment.")
        });
    }

    /// <summary>Backward compat.</summary>
    public static (string Text, object[] Blocks) MutinyStarted(string environment)
    {
        return ($"Mutiny initiated for {environment}", new object[]
        {
            Section($":pirate_flag:  *Mutiny Initiated*"),
            Section($"Preparing to take over `{environment}`."),
            Context("You'll be asked to confirm the takeover")
        });
    }

    public static (string Text, object[] Blocks) MutinyNotClaimed(string environment)
    {
        return ($"{environment} not claimed", new object[]
        {
            Section($":information_source:  `{environment}` is not claimed."),
            Context($"Use `/knutr claim {environment}` to claim it directly")
        });
    }

    public static (string Text, object[] Blocks) MutinyOwnClaim(string environment)
    {
        return ($"You own {environment}", new object[]
        {
            Section($":thinking_face:  You already own `{environment}`."),
            Section("No mutiny needed!")
        });
    }

    public static (string Text, object[] Blocks) ExpiryCheckStarted(string? environment)
    {
        var target = string.IsNullOrEmpty(environment) ? "all stale claims" : $"`{environment}`";
        return ($"Checking {target}", new object[]
        {
            Section($":clock3:  *Expiry Check Started*"),
            Section($"Checking {target} for stale claims...")
        });
    }

    public static (string Text, object[] Blocks) UsageError(string usage, string? hint = null)
    {
        var blocks = new List<object>
        {
            Section($":grey_question:  *Usage*"),
            Section($"`{usage}`")
        };

        if (!string.IsNullOrEmpty(hint))
        {
            blocks.Add(Context(hint));
        }

        return ("Usage info", blocks.ToArray());
    }

    public static (string Text, object[] Blocks) ClaimHelp()
    {
        return ("Environment Claims Help", new object[]
        {
            Section(":lock:  *Environment Claims*"),
            Divider(),
            Section(
                "*Basic Commands*\n" +
                "`/knutr claim [env] [note?]` - Claim an environment\n" +
                "`/knutr release [env]` - Release your claim\n" +
                "`/knutr claimed` - List all claims\n" +
                "`/knutr myclaims` - List your claims"
            ),
            Section(
                "*Interactive*\n" +
                "`/knutr nudge [env]` - Ask owner to release\n" +
                "`/knutr mutiny [env]` - Force takeover"
            ),
            Section(
                "*Examples*\n" +
                "• `/knutr claim demo Testing new feature`\n" +
                "• `/knutr nudge staging`\n" +
                "• `/knutr release demo`"
            )
        });
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
            return $"{(int)duration.TotalDays}d {duration.Hours}h";
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        return $"{(int)duration.TotalMinutes}m";
    }
}

/// <summary>State for mutiny workflow dashboard updates.</summary>
public enum MutinyState
{
    Pending,
    Confirmed,
    Cancelled
}
