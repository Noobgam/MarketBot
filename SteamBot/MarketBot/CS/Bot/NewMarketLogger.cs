using CSGOTM;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using SteamBot.MarketBot.Utility.MongoApi;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static CSGOTM.TMBot;
using static SteamBot.Log;

namespace SteamBot.MarketBot.CS.Bot {
    public class NewMarketLogger {
        public class LogMessage {
            [BsonId]
            public ObjectId ID { get; set; }

            [BsonElement("botname")]
            public string Name { get; set; }

            [BsonElement("type")]
            public string Type { get; set; }

            [BsonElement("ts")]
            public BsonTimestamp TimeStamp { get; set; }

            [BsonElement("message")]
            public string Message { get; set; }

            public override string ToString() {
                DateTime now = new DateTime(1970, 1, 1).Add(new TimeSpan(0, 0, TimeStamp.Increment));
                return $"[{Name} {now.ToString("yyyy-MM-dd HH:mm:ss")}  {Message}";
            }
        }

        private TMBot bot;
        private MongoLogCollection logCollection;

        public NewMarketLogger(TMBot bot) {
            this.bot = bot;
            logCollection = new MongoLogCollection();
        }

        public NewMarketLogger() {
            logCollection = new MongoLogCollection();
        }

        private readonly DateTime epoch = new DateTime(1970, 1, 1);

        private LogMessage CreateRawLogMessage(LogLevel level, string line, params object[] formatParams) {
            if (bot != null)
                return CreateRawLogMessage(level, bot.config.Username, line, formatParams);
            else
                return CreateRawLogMessage(level, "EXTRA", line, formatParams);
        }

        private LogMessage CreateRawLogMessage(LogLevel level, string botname, string line, params object[] formatParams) {
            DateTime instant = DateTime.Now;
            if (level != LogLevel.Nothing) {
                string formattedString = String.Format(
                    "[{0} {1}] {2}: {3}",
                    botname,
                    instant.ToString("yyyy-MM-dd HH:mm:ss"),
                    _LogLevel(level).ToUpper(), (formatParams != null && formatParams.Any() ? String.Format(line, formatParams) : line)
                    );
                _OutputLineToConsole(level, formattedString);
            }
            return new LogMessage {
                ID = new ObjectId(),
                Name = botname,
                Type = _LogLevel(level).ToUpper(),
                Message = formatParams.Any() ? String.Format(line, formatParams) : line,
                TimeStamp = new BsonTimestamp((int)instant.Subtract(epoch).TotalSeconds)
            };
        }

        public void Success(RestartPriority prior, string data, params object[] formatParams) {
            bot.FlagError(prior, data);
            Success(data, formatParams);
        }

        public void Success(string data, params object[] formatParams) {
            logCollection.Insert(CreateRawLogMessage(LogLevel.Success, data, formatParams));
        }

        public void Warn(RestartPriority prior, string data, params object[] formatParams) {
            bot.FlagError(prior, data);
            Warn(data, formatParams);
        }

        public void Warn(string data, params object[] formatParams) {
            logCollection.Insert(CreateRawLogMessage(LogLevel.Warn, data, formatParams));
        }

        public void Info(RestartPriority prior, string data, params object[] formatParams) {
            bot.FlagError(prior, data);
            Info(data, formatParams);
        }

        public void Info(string data, params object[] formatParams) {
            logCollection.Insert(CreateRawLogMessage(LogLevel.Info, data, formatParams));
        }

        public void Info(string botname, string data, params object[] formatParams) {
            logCollection.Insert(CreateRawLogMessage(LogLevel.Info, botname, data, formatParams));
        }

        public void ApiError(RestartPriority prior, string data, params object[] formatParams) {
            bot.FlagError(prior, data);
            ApiError(data, formatParams);
        }

        public void ApiError(string data, params object[] formatParams) {
            logCollection.Insert(CreateRawLogMessage(LogLevel.ApiError, data, formatParams));
        }

        public void Error(RestartPriority prior, string data, params object[] formatParams) {
            bot.FlagError(prior, data);
            Error(data, formatParams);
        }

        public void Error(string data, params object[] formatParams) {
            logCollection.Insert(CreateRawLogMessage(LogLevel.Error, data, formatParams));
        }

        public void Nothing(RestartPriority prior, string data, params object[] formatParams) {
            bot.FlagError(prior, data);
            Nothing(data, formatParams);
        }

        public void Nothing(string data, params object[] formatParams) {
            logCollection.Insert(CreateRawLogMessage(LogLevel.Nothing, data, formatParams));
        }
    }
}
