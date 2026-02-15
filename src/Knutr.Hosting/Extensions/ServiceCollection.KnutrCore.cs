using Knutr.Core.Messaging;
using Knutr.Core.Orchestration;
using Knutr.Core.Replies;
using Knutr.Infrastructure.Prompts;
using Knutr.Abstractions.NL;
using Knutr.Abstractions.Replies;
using Knutr.Abstractions.Plugins;

namespace Knutr.Hosting.Extensions;

public static class KnutrCoreExtensions
{
    public static IServiceCollection AddKnutrCore(this IServiceCollection services, IConfiguration cfg)
    {
        // bus
        services.AddSingleton<IEventBus, InMemoryEventBus>();

        // command registry + router
        services.AddSingleton<ICommandRegistry, CommandRegistry>();
        services.AddSingleton<CommandRouter>();

        // addressing rules from config
        services.AddSingleton(sp =>
        {
            var display = cfg.GetValue<string>("Knutr:DisplayName") ?? "Knutr";
            var botUserId = cfg.GetValue<string>("Slack:BotUserId") ?? "";
            var aliases = cfg.GetSection("Knutr:Aliases").Get<string[]>() ?? ["knutr", "knoot"];
            var replyInDMs = cfg.GetValue<bool?>("Knutr:Addressing:ReplyInDMs") ?? true;
            var replyOnTag = cfg.GetValue<bool?>("Knutr:Addressing:ReplyOnTag") ?? true;
            return new AddressingRules(display, botUserId, aliases, replyInDMs, replyOnTag);
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
file sealed class SimpleNaturalLanguageEngine(ISystemPromptProvider sp, ILlmClient llm) : INaturalLanguageEngine
{
    public async Task<Reply> GenerateAsync(NlMode mode, string? text = null, string? style = null, object? context = null, CancellationToken ct = default)
    {
        var sys = sp.BuildSystemPrompt(style);
        var prompt = text ?? "Be helpful and concise.";
        var result = await llm.CompleteAsync(sys, prompt, ct);
        return new Reply(result, Markdown:false);
    }
}
