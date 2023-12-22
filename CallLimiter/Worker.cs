using ConsoleAppDotNet6;
using Genesys.Client.Notifications;
using Genesys.Client.Notifications.Clients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Reactive.Linq;

namespace CallLimiter
{
    internal class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;

        }

        private ConnectionMultiplexer? _redis;
        private ConnectionMultiplexer GetClient()
        {
            try
            {
                return _redis ?? (_redis = ConnectionMultiplexer.Connect("localhost"));

            }
            catch (Exception ex)
            {
                _logger.LogError($"GetClient failed:{ex.Message}", DateTimeOffset.Now);
                Environment.Exit(-1);
               
            }
            return null;

        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            _logger.LogInformation("=====Notification set up=====");

            // notification Config
            var appConfigName = "appsettings.json";
            var builder = new ConfigurationBuilder()
                         .AddJsonFile(appConfigName, optional: false, reloadOnChange: true);

            var configurations = builder.Build();
            var genesysConfig = configurations.Get<GenesysConfig>();
            var appConfig = configurations.Get<PurecloudAppConfig>();
            var client = new GenesysHttpClient(genesysConfig);
 
            List<KeyValuePair<string, string>> qStatsData = new List<KeyValuePair<string, string>>();

            // receive notification
            try
            {
                using (var topics = new GenesysTopics(client, _logger))
                {
					
					string[] queuesIDs = Fetch_queueIDList();
					while (queuesIDs.Length == 0)
					{
						_logger.LogWarning($"No queueIDList were specified.");
						await Task.Delay(30000, stoppingToken);
						queuesIDs = Fetch_queueIDList();
					}

					using (var notifications = await topics
                        .Add<NotificationData<AnalyticsQueueAggregates>>("v2.analytics.queues.{id}.observations", queuesIDs)
                        .CreateAsync())
                    {
                        _logger.LogInformation("=====Notification receiving=====");


                        notifications.Streams.Domain
                            .OfType<NotificationData<AnalyticsQueueAggregates>>()
                            .Subscribe(async e =>
                            {
                                var data = e.EventBody.data.Select(x => x.metrics).FirstOrDefault();
                                var media = e.EventBody.group.mediaType;
                                
                                if (data != null)
                                {
                                    var hasInteracting = data.Any(x => x.metric == "oInteracting"||x.metric== "oWaiting");
									var hasAvailAgents = data.Any(x => x.metric == "oUserRoutingStatuses");
									var isInQueueIds = queuesIDs.Any(x => x.Contains(e.EventBody.group.queueId));
                                    
                                    if (hasInteracting && isInQueueIds && media == "voice")
                                    {
                                        int waitingCount = 0;
                                        int interacting = 0;
                                        int wtgintr = 0;

                                        interacting = (int)data.Where(x => x.metric == "oInteracting").Select(x => x.stats.Count).FirstOrDefault();
                                        waitingCount = (int)data.Where(x => x.metric == "oWaiting").Select(x => x.stats.Count).FirstOrDefault();
										wtgintr = waitingCount+ interacting;

										string receivedQueueID = e.EventBody.group.queueId.ToString();
                                        string key = "qStats:"+ receivedQueueID;

                                        List<KeyValuePair<string, string>> keyValuePairs = new List<KeyValuePair<string, string>>
                                        {
                                            new KeyValuePair<string, string>("wtg", waitingCount.ToString()),
                                            new KeyValuePair<string, string>("intr", interacting.ToString()),
                                            new KeyValuePair<string, string>("wtgintr", wtgintr.ToString()),
											
										};
                                        _logger.LogInformation("qID:{0} wtg:{1} intr:{2} wtgintr:{3} time:{4}", receivedQueueID, waitingCount, interacting, wtgintr, DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"));

                                        await HashSetAsync(key, keyValuePairs);

                                    }

									if (hasAvailAgents && isInQueueIds)
									{
										string availagt = "0";

                                        availagt = data.Where(x => x.metric == "oUserRoutingStatuses" && x.qualifier == "IDLE").Select(x => x.stats.Count).FirstOrDefault().ToString();

										string receivedQueueID = e.EventBody.group.queueId.ToString();
										string key = "qStats:" + receivedQueueID;

										List<KeyValuePair<string, string>> keyValuePairs = new List<KeyValuePair<string, string>>
										{
											new KeyValuePair<string, string>("availagt", availagt)
										};
										_logger.LogInformation("qID:{0} availagt:{1} time:{2}", receivedQueueID, availagt, DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"));

										await HashSetAsync(key, keyValuePairs);

									}

								}

                            });

                        notifications.Streams.Pong.Subscribe(_ =>
                        {
                            //_logger.LogInformation("PONG", DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                        });
                        notifications.Streams.Heartbeats.Subscribe(_ =>
                        {
                            _logger.LogInformation("HEART BEAT");

                        });
                        notifications.Streams.SocketClosing.Subscribe(_ =>
                        {
                            _logger.LogInformation("Socket closing");

                        });
                        notifications.Streams.System.Subscribe(s => _logger.LogInformation($"{s.SystemTopicType}{s.Reason}{s.ChannelId}{s.UserId}"));
                        //notifications.Ping();

                        while (!stoppingToken.IsCancellationRequested)
                        {
                            notifications.Ping();
                            _logger.LogInformation("ping at: {time}", DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                            await Task.Delay(30000, stoppingToken);

                            if (await Update_queueIDList(queuesIDs))
                            {
                                queuesIDs = Fetch_queueIDList();
                                if (queuesIDs.Length == 0)
                                {
                                    _logger.LogError($"No queueIDList were specified.");
									continue;
								}

                                await topics.UpdateAsync<NotificationData<AnalyticsQueueAggregates>>("v2.analytics.queues.{id}.observations", queuesIDs);

                                foreach (var item in queuesIDs)
                                {
									_logger.LogInformation($"Update Subscription:{item}");
								}

                                await notifications.UpdateSubscriptionsAsync();

                                _logger.LogInformation("queueIDList has been changed. Notification Updated.");

                            }
                            else
                            {
                                _logger.LogInformation("queueIDList has NOT been changed.");
                            }

                        }

                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"ExecuteAsync failed:{ex.Message}", DateTimeOffset.Now);
                throw;
            }

        }

        public async ValueTask HashSetAsync(string key, List<KeyValuePair<string, string>> keyValuePairs)
        {

            try
            {
                var db = GetClient().GetDatabase();
                var hashEntries = keyValuePairs.Select(x => new HashEntry(x.Key, x.Value)).ToArray();
                await db.HashSetAsync(key, hashEntries);
                string output = string.Join(", ", hashEntries.Select(entry => $"Key: {entry.Name}, Value: {entry.Value}"));
                _logger.LogInformation("qStats DB Updated {0} at {1}", output, DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"));

            }
            catch (Exception ex)
            {
                _logger.LogError($"HashSetAsync failed:{ex.Message}", DateTimeOffset.Now);
                throw;
            }

        }

        public string[] Fetch_queueIDList()
        {
            try
            {
                var db = GetClient().GetDatabase();
                var redisQueueIDList = db.SetMembers("queueIDList");
                if (redisQueueIDList == null || redisQueueIDList.Length == 0) return new string[0];

                return Array.ConvertAll(redisQueueIDList, x => x.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError($"Fetch_queueIDList failed:{ex.Message}", DateTimeOffset.Now);

                throw;
            }

        }

		public async Task<bool> Update_queueIDList(string[] queueIDs)
		{
            try
            {
                var db = GetClient().GetDatabase();
                var redisQueueIDList = db.SetMembers("queueIDList");

                var queueIDSet = new HashSet<string>(queueIDs);
                var redisQueueIDSet = new HashSet<string>(redisQueueIDList.Select(id => id.ToString()));

                bool isUpdated = false;

                // Find newly added queue ID and run hashSetAsync
                foreach (var id in redisQueueIDSet.Except(queueIDSet))
                {
                    string key = "qStats:" + id;
                    List<KeyValuePair<string, string>> keyValuePairs = new List<KeyValuePair<string, string>>
                    {
                        new KeyValuePair<string, string>("wtg", "0"),
                        new KeyValuePair<string, string>("intr", "0"),
                        new KeyValuePair<string, string>("wtgintr", "0"),
                        new KeyValuePair<string, string>("availagt", "0")

                    };

                    _logger.LogInformation($"Update_queueIDList new queueID {id} is found. create new qStats DB.");
                    await HashSetAsync(key, keyValuePairs);

                    isUpdated = true;
                }

                // Run keyDeleteAsync if queue ID does not exist
                foreach (var id in queueIDSet.Except(redisQueueIDSet))
                {
                    string key = "qStats:" + id;
                    await db.KeyDeleteAsync(key);
                    _logger.LogInformation($"Update_queueIDList queueID {id} is deleted.");
                    isUpdated = true;
                }

                return isUpdated;

            }
            catch (Exception ex)
            {
                _logger.LogError($"Update_queueIDList failed:{ex.Message}", DateTimeOffset.Now);
                throw;
            }


		}


	}
}