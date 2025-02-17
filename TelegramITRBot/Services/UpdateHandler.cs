using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramITRBot.Configs;
using System.Collections.Concurrent;

namespace TelegramITRBot.Services;

public class UpdateHandler : IUpdateHandler
{
    private readonly ILogger<UpdateHandler> _logger;
    private readonly TelegramBotConfig _telegramBotConfig;
    private readonly ITelegramBotClient _botClient;
    private readonly IMessageService _messageService;

    private readonly ConcurrentDictionary<long, List<string>> _userAnswers = new();
    private readonly ConcurrentDictionary<long, int> _userSteps = new();

    private readonly List<string> _questions = new()
    {
        "What is your name?",
        "What is your favorite movie?"
    };

    public UpdateHandler(
        ILogger<UpdateHandler> logger,
        IOptions<TelegramBotConfig> telegramBotConfig,
        ITelegramBotClient botClient,
        IMessageService messageService
    )
    {
        _logger = logger;
        _telegramBotConfig = telegramBotConfig.Value;
        _botClient = botClient;
        _messageService = messageService;
    }

    public async Task HandleUpdateAsync(ITelegramBotClient _, Update update, CancellationToken cancellationToken)
    {
        if (update.Message is { } message && message.Type == MessageType.Text)
        {
            await BotOnMessageReceived(message, cancellationToken);
        }
    }

    private async Task BotOnMessageReceived(Message message, CancellationToken cancellationToken)
    {
        var userId = message.From!.Id;

        if (message.Text!.StartsWith("/start") || message.Text!.StartsWith("/start_survey"))
        {
            _userAnswers[userId] = new List<string>();
            _userSteps[userId] = 0;
            await _botClient.SendTextMessageAsync(message.Chat.Id, _questions[0], cancellationToken: cancellationToken);
            return;
        }

        if (_userSteps.ContainsKey(userId))
        {
            _userAnswers[userId].Add(message.Text);

            int nextStep = _userSteps[userId] + 1;
            if (nextStep < _questions.Count)
            {
                _userSteps[userId] = nextStep;
                await _botClient.SendTextMessageAsync(message.Chat.Id, _questions[nextStep], cancellationToken: cancellationToken);
            }
            else
            {
                await _botClient.SendTextMessageAsync(message.Chat.Id, "Survey completed!", cancellationToken: cancellationToken);
                
                await SendSurveyResults(userId, message.From!, cancellationToken);
                
                _userAnswers.TryRemove(userId, out _);
                _userSteps.TryRemove(userId, out _);
            }
        }
    }

    private async Task SendSurveyResults(long userId, User user, CancellationToken cancellationToken)
    {
        if (!_userAnswers.TryGetValue(userId, out var answers) || answers.Count < _questions.Count)
            return;

        var userInfo = $"User info:\n\n" +
                       $"ID: {user.Id}\n" +
                       $"Name: {user.FirstName}\n" +
                       $"Surname: {user.LastName ?? "not set"}\n" +
                       $"Username: @{user.Username ?? "not set"}\n" +
                       $"Language Code: {user.LanguageCode ?? "not set"}\n" +
                       $"Is premium: {(user.IsPremium.HasValue && user.IsPremium.Value ? "YES" : "NO")}\n" +
                       $"Is added to attach menu: {(user.AddedToAttachmentMenu.HasValue && user.AddedToAttachmentMenu.Value ? "YES" : "NO")}\n" +
                       $"Is user a bot: {(user.IsBot ? "YES" : "NO")}";


        var report = $"{userInfo}\n\n";
        for (var i = 0; i < _questions.Count; i++)
        {
            report += $"{_questions[i]}\n{answers[i]}\n\n";
        }

        await _botClient.SendTextMessageAsync(
            chatId: _telegramBotConfig.PrivateChatId,
            text: report,
            cancellationToken: cancellationToken);
    }

    public async Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        _logger.LogError("Error: {ErrorMessage}", exception.ToString());
    }
}
