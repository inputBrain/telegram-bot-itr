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
        "Підтвердіть номер телефону", // [3]
        "Виберіть спосіб оплати", // [4]
    };

    private readonly List<Func<Message, CancellationToken, Task<bool>>> _stepHandlers;

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

        _stepHandlers = new List<Func<Message, CancellationToken, Task<bool>>>
        {
            HandleSubscriptionStep,
            HandleAddressStep,
            HandleTimeStep,
            HandlePhoneStep,
            HandlePaymentStep
        };
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

        if (message.Text != null && (message.Text.StartsWith("/start") || message.Text.StartsWith("/request_services")))
        {
            _userAnswers[userId] = new QuestionModel();
            _userSteps[userId] = 0;

            await _botClient.SendTextMessageAsync(message.Chat.Id, "Вітаємо!", cancellationToken: cancellationToken);
            await _botClient.SendTextMessageAsync(message.Chat.Id, "Набридло виносити щодня сміття? Ми за Вас це зробимо!\n\nМи — компанія <company>, зробимо Ваш день приємніше і зручніше!\n\nОформлюйте разове замовлення або підписку, і наш курʼєр забере Ваше сміття.", cancellationToken: cancellationToken);

            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton[] { "Разовий вивіз (40 грн)" },
                new KeyboardButton[] { "Підписка (1000 грн / 1 місяць)" },
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
            var currentStep = _userSteps[userId];
            if (currentStep < _stepHandlers.Count)
            {
                var success = await _stepHandlers[currentStep](message, cancellationToken);
                if (success)
                {
                    _userSteps[userId]++;
                    if (_userSteps[userId] < _questions.Count)
                    {
                        await _botClient.SendTextMessageAsync(message.Chat.Id, _questions[_userSteps[userId]], cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await FinalizeSurvey(userId, message, cancellationToken);
                    }
                }
            }
        }
    }

    private Task<bool> HandleSubscriptionStep(Message message, CancellationToken cancellationToken)
    {
        var userId = message.From!.Id;
        _userAnswers[userId].OnSubscribe = message.Text!;
        return Task.FromResult(true);
    }

    private Task<bool> HandleAddressStep(Message message, CancellationToken cancellationToken)
    {
        var userId = message.From!.Id;
        _userAnswers[userId].Address = message.Text!;
        return Task.FromResult(true);
    }

    private Task<bool> HandleTimeStep(Message message, CancellationToken cancellationToken)
    {
        var userId = message.From!.Id;
        _userAnswers[userId].TimeToPickup = message.Text!;
        return Task.FromResult(true);
    }

    private async Task<bool> HandlePhoneStep(Message message, CancellationToken cancellationToken)
    {
        var userId = message.From!.Id;
        var phonePattern = @"^(?:\+?380\d{9}|0\d{9})$";

        if (message.Text != null && Regex.IsMatch(message.Text.Replace(" ", ""), phonePattern))
        {
            _userAnswers[userId].PhoneNumber = message.Text.Replace(" ", "");
            return true;
        }

        await _botClient.SendTextMessageAsync(
            chatId: message.Chat.Id,
            text: "❌ Номер телефону некоректний. Введіть номер в одному з форматів:\n\n✅ 093 000 00 00\n✅ 38 093 000 00 00\n✅ +38 093 000 00 00",
            cancellationToken: cancellationToken);
        return false;
    }

    private async Task<bool> HandlePaymentStep(Message message, CancellationToken cancellationToken)
    {
        var userId = message.From!.Id;
        if (message.Text != null && (message.Text == "Готівкою на місці" || message.Text == "Картою на місці" || message.Text == "По предоплаті на карту"))
        {
            _userAnswers[userId].TypeToPay = message.Text;
            return true;
        }

        var keyboard = new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "Готівкою на місці" },
            new KeyboardButton[] { "Картою на місці"},
            new KeyboardButton[] { "По предоплаті на карту" }
        })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };

        await _botClient.SendTextMessageAsync(
            chatId: message.Chat.Id,
            text: _questions[4],
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
        return false;
    }

    private async Task FinalizeSurvey(long userId, Message message, CancellationToken cancellationToken)
    {
        await _botClient.SendTextMessageAsync(message.Chat.Id, "Додаткова інформація на гарячій лінії ...", cancellationToken: cancellationToken);
        await _botClient.SendTextMessageAsync(message.Chat.Id, "Якщо у вас виникли питання, чи потрібна додаткова інформація напишіть нашому менеджеру:", cancellationToken: cancellationToken);
        
        await SendSurveyResults(userId, message.From!, cancellationToken);
        
        _userAnswers.TryRemove(userId, out _);
        _userSteps.TryRemove(userId, out _);
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
                     $"Номер телефону: {answers.PhoneNumber}\n" +
                     $"Спосіб оплати: {answers.TypeToPay}";

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