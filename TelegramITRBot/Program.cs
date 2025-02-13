using Telegram.Bot;
using TelegramITRBot.Configs;
using TelegramITRBot.Services;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(
        config =>
        {
            config.AddJsonFile("appsettings.json")
                .Build();
        })
    .ConfigureServices(
        (hostContext, services) =>
        {

            var telegramConfig = hostContext.Configuration.GetSection("TelegramBot").Get<TelegramBotConfig>();
            if (telegramConfig == null)
            {
                throw new Exception("\n\n -----ERROR ATTENTION! ----- \n Telegram bot config 'TelegramBot' is null or does not exist. \n\n");
            }

            services.Configure<TelegramBotConfig>(hostContext.Configuration.GetSection("TelegramBot"));

            var botClient = new TelegramBotClient(telegramConfig.BotToken);

            services.AddSingleton<ITelegramBotClient>(botClient);

            services.AddSingleton<IMessageService, MessageService>();

            services.AddScoped<UpdateHandler>();
            services.AddScoped<ReceiverService>();
            services.AddHostedService<PollingService>();

        })
    .Build();

host.Run();