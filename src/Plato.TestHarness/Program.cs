﻿
namespace Plato.TestHarness
{
    class Program
    {
        static void Main(string[] args)
        {
            // ConfigurationTest.ConfigurationPlayground.RunAsync().GetAwaiter();
            // RedisTest.RedisPlayground.RunAsync().GetAwaiter();     

            Messaging.RMQPlayground.RunAsync().GetAwaiter();
            // Messaging.RMQPlayground.Run();

            // Messaging.AMQPlayground.RunAsync().GetAwaiter();
            // Messaging.AMQPlayground.Run();

            // Mapper.MapperPlayground.RunAsync().GetAwaiter();

            // DomainEventsTest.DomainEventsPlayground.RunAsync().GetAwaiter();
        }
    }
}
