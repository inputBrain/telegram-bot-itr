using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramITRBot.Configs;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using TelegramITRBot.Models;

namespace TelegramITRBot.Services;

public class UpdateHandler : IUpdateHandler
{
    private readonly ILogger<UpdateHandler> _logger;
    private readonly TelegramBotConfig _telegramBotConfig;
    private readonly ITelegramBotClient _botClient;
    private readonly IMessageService _messageService;

    private readonly ConcurrentDictionary<long, QuestionModel> _userAnswers = new();
    private readonly ConcurrentDictionary<long, int> _userSteps = new();

    private readonly List<string> _questions = new()
    {
        "Оберіть разовий вивіз сміття чи підписку", // [0]
        "Залиште Вашу адресу, підʼїзд, поверх, квартиру", // [1]
        "В який час Вам буде зручно щоб ми забрали сміття?", // [2]
        "підтвердіть номер телефону", // [3
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
        if (update.Message is { } message)
        {
            await BotOnMessageReceived(message, cancellationToken);
        }
    }

    private async Task BotOnMessageReceived(Message message, CancellationToken cancellationToken)
    {
        var userId = message.From!.Id;

        if (message.Text != null && (message.Text.StartsWith("/start") || message.Text.StartsWith("/start_survey")))
        {
            _userAnswers[userId] = new QuestionModel();
            _userSteps[userId] = 0;

            // 1
            await _botClient.SendTextMessageAsync(message.Chat.Id, "Вітаємо!", cancellationToken: cancellationToken); 
            // 2
            await _botClient.SendTextMessageAsync(message.Chat.Id, "Набридло виносити щодня сміття? Ми за Вас це зробимо!\n\nМи — компанія <company>, зробимо Ваш день приємніше і зручніше!\n\nОформлюйте разове замовлення або підписку, і наш курʼєр забере Ваше сміття.", cancellationToken: cancellationToken);

            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "Разовий вивіз" },
                new KeyboardButton[] { "Підписка" },
                // new KeyboardButton[] { "Разовий вивіз" }, ["Підписка"]
            })
            {
                ResizeKeyboard = true,
                OneTimeKeyboard = true
            };

            await _botClient.SendTextMessageAsync(
                chatId: message.Chat.Id,
                text: _questions[0],
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
            return;
        }
        
        if (_userSteps.ContainsKey(userId))
        {
            if (_userSteps[userId] == 3)
            {
                var phonePattern = @"^(?:\+?380\d{9}|0\d{9})$"; // 380930000000, 0930000000, +380930000000

                if (message.Text != null && Regex.IsMatch(message.Text.Replace(" ", ""), phonePattern))
                {
                    _userAnswers[userId].PhoneNumber = message.Text.Replace(" ", "");
                    _userSteps[userId]++;
                }
                else
                {
                    await _botClient.SendTextMessageAsync(
                        chatId: message.Chat.Id,
                        text: "❌ Номер телефону некоректний. Введіть номер в одному з форматів:\n\n✅ 093 000 00 00\n✅ 38 093 000 00 00\n✅ +38 093 000 00 00",
                        cancellationToken: cancellationToken);
                    return;
                }
            }
            switch (_userSteps[userId])
            {
                case 0:
                    _userAnswers[userId].OnSubscribe = message.Text;
                    break;
                case 1:
                    _userAnswers[userId].Address = message.Text;
                    break;
                case 2:
                    _userAnswers[userId].TimeToPickup = message.Text;
                    break;
                case 3:
                    _userAnswers[userId].PhoneNumber = message.Text;
                    break;
            }
            

            var nextStep = _userSteps[userId] + 1;
            if (nextStep < _questions.Count)
            {
                _userSteps[userId] = nextStep;
                
                    await _botClient.SendTextMessageAsync(message.Chat.Id, _questions[nextStep], cancellationToken: cancellationToken);
                
            }
            else
            {
                await _botClient.SendTextMessageAsync(message.Chat.Id, "Додаткова інформація на гарячій лінії ...", cancellationToken: cancellationToken);
                // await _botClient.SendTextMessageAsync(message.Chat.Id, "\nTODO: Checkout", cancellationToken: cancellationToken);
                await SendSurveyResults(userId, message.From!, cancellationToken);
                _userAnswers.TryRemove(userId, out _);
                _userSteps.TryRemove(userId, out _);
            }
        }
    }

    private async Task SendSurveyResults(long userId, User user, CancellationToken cancellationToken)
    {
        if (!_userAnswers.TryGetValue(userId, out var answers)) return;

        var report = $"User info:\n" +
                     $"ID: {user.Id}\n" +
                     $"Name: {user.FirstName}\n" +
                     $"Surname: {user.LastName ?? "not set"}\n" +
                     $"Username: @{user.Username ?? "not set"}\n" +
                     $"Language Code: {user.LanguageCode ?? "not set"}\n\n" +
                     $"{answers.OnSubscribe}\n" +
                     $"Адреса: {answers.Address}\n" +
                     $"Час вивозу: {answers.TimeToPickup}\n" +
                     $"Номер телефону: {answers.PhoneNumber}";

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
