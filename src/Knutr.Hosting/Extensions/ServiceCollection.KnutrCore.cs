using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Knutr.Core.Messaging;
using Knutr.Core.Orchestration;
using Knutr.Core.Replies;
using Knutr.Core.Observability;
using Knutr.Infrastructure.Prompts;
using Knutr.Abstractions.NL;

namespace Knutr.Hosting.Extensions;

public static class KnutrCoreExtensions
{
    public static IServiceCollection AddKnutrCore(this IServiceCollection services, IConfiguration cfg)
    {
        // metrics
        services.AddSingleton<CoreMetrics>();

        // bus
        services.AddSingleton<IEventBus, InMemoryEventBus>();

        // command registry + router
        services.AddSingleton<ICommandRegistry, CommandRegistry>();
        services.AddSingleton<CommandRouter>();

        // addressing rules from config
        services.AddSingleton(sp =>
        {
            var display = cfg.GetValue<string>("Knutr:DisplayName") ?? "Knutr";
            var aliases = cfg.GetSection("Knutr:Aliases").Get<string[]>() ?? new[] { "knutr" };
            var replyInDMs = cfg.GetValue<bool?>("Knutr:Addressing:ReplyInDMs") ?? true;
            var replyOnTag = cfg.GetValue<bool?>("Knutr:Addressing:ReplyOnTag") ?? true;
            // Slack adapter can inject real BotUserId later; keep blank by default
            return new AddressingRules(display, "", aliases, replyInDMs, replyOnTag);
        });

        // NL + prompt provider (engine stub uses ILlmClient via hosting LLM reg)
        services.AddSingleton<ISystemPromptProvider, ConfigPromptProvider>();
        services.AddSingleton<INaturalLanguageEngine, SimpleNaturalLanguageEngine>();

        // reply + progress
        services.AddSingleton<IReplyService, ReplyService>();

        // orchestrator
        services.AddSingleton<ChatOrchestrator>();

        return services;
    }
}

// Simple NL engine calling ILlmClient; minimal implementation
file sealed class SimpleNaturalLanguageEngine : INaturalLanguageEngine
{
    private readonly ISystemPromptProvider _sp;
    private readonly Knutr.Abstractions.NL.ILlmClient _llm;
    public SimpleNaturalLanguageEngine(ISystemPromptProvider sp, Knutr.Abstractions.NL.ILlmClient llm) { _sp = sp; _llm = llm; }

    public async Task<Knutr.Abstractions.Replies.Reply> GenerateAsync(Knutr.Abstractions.Plugins.NlMode mode, string? text = null, string? style = null, object? context = null, CancellationToken ct = default)
    {
        var sys = _sp.BuildSystemPrompt(style);
        var prompt = text ?? "Be helpful.";
        var result = await _llm.CompleteAsync(sys, prompt, ct);
        return new Knutr.Abstractions.Replies.Reply(result, Markdown:false);
    }
}
