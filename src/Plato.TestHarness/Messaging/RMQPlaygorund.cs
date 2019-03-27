﻿using Plato.Messaging.Enums;
using Plato.Messaging.Exceptions;
using Plato.Messaging.Interfaces;
using Plato.Messaging.RMQ;
using Plato.Messaging.RMQ.Factories;
using Plato.Messaging.RMQ.Interfaces;
using Plato.Messaging.RMQ.Pool;
using Plato.Messaging.RMQ.Settings;
using Plato.Miscellaneous;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Plato.TestHarness.Messaging
{
    public class RMQPlayground
    {
        // http://zoltanaltfatter.com/2016/09/06/dead-letter-queue-configuration-rabbitmq/

        static RMQConfigurationManager CreateConfigurationManager()
        {
            var connectionSettings = new RMQConnectionSettings
            {
                Name = "connection",
                Username = "guest",
                Password = "guest",
                VirtualHost = "/",
                Uri = "amqp://host.docker.internal:5672",
                DelayOnReconnect = 2000,
            };

            var queueSettings = new RMQQueueSettings("MY_RMQ_TEST", "MY_RMQ_TEST")
            {                
                Durable = true,
                AutoDelete = false,
                Persistent = true,
                Exclusive = false,                       
            };

            return new RMQConfigurationManager(new[] { connectionSettings }, queueSettings: new[] { queueSettings });
        }             

        static private Task ProducerAsync()
        {
            var args = new Dictionary<string, object>
            {
                { "x-dead-letter-exchange", "" },
                { "x-dead-letter-routing-key", "DLQ_MY_RMQ_TEST" }
            };

            var configManager = CreateConfigurationManager();            
            var procuderFactory = new RMQProducerFactory(new RMQConnectionFactory());
            var connectionSettings = configManager.GetConnectionSettings("connection");
            var queueSettings = configManager.GetQueueSettings("MY_RMQ_TEST", args); 

            using (var producer = procuderFactory.CreateText(connectionSettings, queueSettings))
            {
                producer.Send("test1");
                producer.Send("test2");
                producer.Send("test3");
                producer.Send("test4");
            }

            return Task.CompletedTask;
        }

        static private async Task ProducerPerformanceTestAsync()
        {
            var args = new Dictionary<string, object>
            {
                { "x-dead-letter-exchange", "" },
                { "x-dead-letter-routing-key", "DLQ_MY_RMQ_TEST" }
            };

            var configManager = CreateConfigurationManager();            
            var procuderFactory = new RMQProducerFactory(new RMQConnectionFactory());
            var connectionSettings = configManager.GetConnectionSettings("connection");
            var queueSettings = configManager.GetQueueSettings("MY_RMQ_TEST", args);

            using (var producer = procuderFactory.CreateText(connectionSettings, queueSettings))
            {
                while (true)
                {
                    try
                    {
                        Console.WriteLine("Provide a command: quit, clear or a number");

                        var command = Console.ReadLine();
                        if (command == "quit" || command == "exit")
                        {
                            break;
                        }

                        if (command == "clear")
                        {
                            Console.Clear();
                            continue;
                        }

                        if (!int.TryParse(command, out int count) || count < 0)
                        {
                            Console.WriteLine("Invalid iteration number");
                            continue;
                        }

                        var sw = new Stopwatch();
                        sw.Reset();
                        sw.Start();

                        for (var i = 0; i < count; i++)
                        {
                            var data = $"Message from Ross: {i}";
                            await producer.SendAsync(data);
                        }

                        sw.Stop();
                        Console.WriteLine($"Done sending: {sw.ElapsedMilliseconds}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                }
            }
        }

        static private Task ConsumerAsync()
        {
            var args = new Dictionary<string, object>
            {
                { "x-dead-letter-exchange", "" },
                { "x-dead-letter-routing-key", "DLQ_MY_RMQ_TEST" }
            };

            var configManager = CreateConfigurationManager();
            var consumerFactory = new RMQConsumerFactory(new RMQConnectionFactory());
            var connectionSettings = configManager.GetConnectionSettings("connection");
            var queueSettings = configManager.GetQueueSettings("MY_RMQ_TEST", args);

            using (var consumer = consumerFactory.CreateText(connectionSettings, queueSettings))
            {
                consumer.Mode = ConsumerMode.OnNoMessage_ReturnNull;

                while (true)
                {
                    try
                    {
                        try
                        {
                            var message = consumer.Receive(1000);
                            if (message != null)
                            {
                                //message.Reject(true);
                                //message.Reject();

                                Console.WriteLine(message.Data);
                                message.Acknowledge();
                            }
                        }
                        catch (TimeoutException)
                        {
                        }
                        catch (MessageException ex)
                        {
                            switch (ex.ExceptionCode)
                            {
                                case MessageExceptionCode.ExclusiveLock:
                                    //await Task.Delay(5000);
                                    break;

                                case MessageExceptionCode.LostConnection:
                                    //await Task.Delay(5000);
                                    break;

                                default:
                                    throw;
                            }
                        }
                        catch (SqlException ex)
                        {
                            if (SQLErrors.IsSevereErrorCode(ex.Number))
                            {
                                // issue connecting with SQL server
                                //await Task.Delay(5000);
                            }

                            throw;
                        }
                    }
                    catch (Exception ex)
                    {
                        consumer.ClearCacheBuffer();
                        Console.WriteLine(ex);
                    }
                }
            }
        }

        #region Pool Test
        static Task PoolTestAsync()
        {
            var args = new Dictionary<string, object>
            {
                { "x-dead-letter-exchange", "" },
                { "x-dead-letter-routing-key", "DLQ_MY_RMQ_TEST" }
            };

            var configManager = CreateConfigurationManager();
            var consumerFactory = new RMQConsumerFactory(new RMQConnectionFactory());
            var producerFactory = new RMQProducerFactory(new RMQConnectionFactory());
            var subscriberFactory = new RMQSubscriberFactory(new RMQConnectionFactory());
            var publisherFactory = new RMQPublisherFactory(new RMQConnectionFactory());
            var factory = new RMQSenderReceiverFactory(consumerFactory, producerFactory, subscriberFactory, publisherFactory);

            using (var amqPool = new RMQPoolAsync(configManager, factory, 5))
            {
                var tasks = new List<Task>();

                for (var i = 0; i < 10; i++)
                {
                    var task = Task.Run(async () =>
                    {
                        try
                        {
                            for (var j = 0; j < 10; j++)
                            {
                                using (var producer = await amqPool.GetAsync<IRMQProducerText>("connection", "MY_RMQ_TEST", queueArgs: args))
                                {
                                    var message = $"message: {i * j}";
                                    await producer.Instance.SendAsync(message);

                                    Console.WriteLine($"Thread: {Thread.CurrentThread.ManagedThreadId} - {producer.PoolId} - {producer.Instance.Id} - {message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex);
                        }
                    });

                    tasks.Add(task);
                }

                Console.WriteLine("Waiting for Tasks to complete...");
                Task.WaitAll(tasks.ToArray());
                Console.WriteLine("Tasks completed.");

                return Task.CompletedTask;
            }
        }

        static void PoolThreadTestThread(object obj)
        {
            var rmqPoolCache = ((Tuple<RMQPool, int>)obj).Item1;
            var i = ((Tuple<RMQPool, int>)obj).Item2;

            var args = new Dictionary<string, object>
            {
                { "x-dead-letter-exchange", "" },
                { "x-dead-letter-routing-key", "DLQ_MY_RMQ_TEST" }
            };

            try
            {
                for (var j = 0; j < 10; j++)
                {
                    using (var producer = rmqPoolCache.Get<IRMQProducerText>("connection", "MY_RMQ_TEST", queueArgs: args))
                    {
                        var message = $"message: {i * j}";
                        producer.Instance.Send(message);

                        Console.WriteLine($"Thread: {Thread.CurrentThread.ManagedThreadId} - {producer.PoolId} - {producer.Instance.Id} - {message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }
            finally
            {
                Console.WriteLine("Test thread completed");
            }
        }

        static void PoolThreadTest()
        {
            var configManager = CreateConfigurationManager();
            var consumerFactory = new RMQConsumerFactory(new RMQConnectionFactory());
            var producerFactory = new RMQProducerFactory(new RMQConnectionFactory());
            var subscriberFactory = new RMQSubscriberFactory(new RMQConnectionFactory());
            var publisherFactory = new RMQPublisherFactory(new RMQConnectionFactory());           
            var factory = new RMQSenderReceiverFactory(consumerFactory, producerFactory, subscriberFactory, publisherFactory);

            using (var rmqPool = new RMQPool(configManager, factory, 2))
            {
                for (var i = 0; i < 10; i++)
                {
                    var t = new Thread(PoolThreadTestThread);
                    t.Start(new Tuple<RMQPool, int>(rmqPool, i));
                }

                Console.WriteLine("Waiting for Tasks to complete...");
                Console.ReadKey();
                Console.WriteLine("Show have terminated.");
            }
        }

        static void SimplePoolTest()
        {
            var args = new Dictionary<string, object>
            {
                { "x-dead-letter-exchange", "" },
                { "x-dead-letter-routing-key", "DLQ_MY_RMQ_TEST" }
            };

            var configManager = CreateConfigurationManager();
            var consumerFactory = new RMQConsumerFactory(new RMQConnectionFactory());
            var producerFactory = new RMQProducerFactory(new RMQConnectionFactory());
            var subscriberFactory = new RMQSubscriberFactory(new RMQConnectionFactory());
            var publisherFactory = new RMQPublisherFactory(new RMQConnectionFactory());
            var factory = new RMQSenderReceiverFactory(consumerFactory, producerFactory, subscriberFactory, publisherFactory);

            using (var rmqPool = new RMQPool(configManager, factory, 5))
            {
                using (var producer = rmqPool.Get<IRMQProducerText>("connection", "MY_RMQ_TEST", queueArgs: args))
                {
                    producer.Instance.Send("Simple test");
                }
            }
        }

        static async Task SimplePoolTestAsync()
        {
            var args = new Dictionary<string, object>
            {
                { "x-dead-letter-exchange", "" },
                { "x-dead-letter-routing-key", "DLQ_MY_RMQ_TEST" }
            };

            var configManager = CreateConfigurationManager();
            var consumerFactory = new RMQConsumerFactory(new RMQConnectionFactory());
            var producerFactory = new RMQProducerFactory(new RMQConnectionFactory());
            var subscriberFactory = new RMQSubscriberFactory(new RMQConnectionFactory());
            var publisherFactory = new RMQPublisherFactory(new RMQConnectionFactory());
            var factory = new RMQSenderReceiverFactory(consumerFactory, producerFactory, subscriberFactory, publisherFactory);

            using (var amqPool = new RMQPoolAsync(configManager, factory, 5))
            {
                using (var producer = await amqPool.GetAsync<IRMQProducerText>("connection", "MY_RMQ_TEST", queueArgs: args))
                {
                    await producer.Instance.SendAsync("Simple test");
                }
            }
        }

        static async Task PoolAsyncManagerReadTestAsync()
        {
            var configuration = new RMQConfigurationManager();
            var pool = new RMQPoolFactory().CreateAsyncPool(configuration, 10);
            var poolManager = new RMQPoolAsyncManager(pool, "connection");

            while (true)
            {
                await poolManager.ReadAsync("test", (IMessageReceiveResult<string> message) =>
                {
                    return Task.CompletedTask;

                }, msecTimeout: 1000);

                //await poolManager.ReadAsync("lms.activity.item", (IMessageReceiveResult<string> message) =>
                //{
                //    return Task.CompletedTask;

                //}, msecTimeout: 1000);
            }

        }


        #endregion Pool Test

        static public async Task RunAsync()
        {
            //await ProducerPerformanceTestAsync();
            //await ProducerAsync();
            //await ConsumerAsync();
            // await PoolTestAsync();
            //await SimplePoolTestAsync();

            // await PoolAsyncManagerReadTestAsync();

            var rmqConnectionSetting = new RMQConnectionSettings
            {
                Name = "connection",
                Username = "Ross UN",
                Password = "Ross PW",
                VirtualHost = "/",
                DelayOnReconnect = 1500,
                Uri = "amqp://some-host:5672"
            };

            var config = new RMQConfigurationManager(new[] { rmqConnectionSetting } );
            var con = config.GetConnectionSettings("connection");
            var queue = config.GetQueueSettings("lms.activity.items");

            await Task.Delay(0);
        }

        static public void Run()
        {
            // PoolThreadTest();
            // SimplePoolTest();
        }
    }
}
