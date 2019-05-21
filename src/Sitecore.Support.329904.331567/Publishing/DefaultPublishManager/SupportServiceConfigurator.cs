using Microsoft.Extensions.DependencyInjection;
using Sitecore.Abstractions;
using Sitecore.DependencyInjection;

namespace Sitecore.Support.Publishing.DefaultPublishManager
{
    public class SupportServiceConfigurator : IServicesConfigurator
    {
        public void Configure(IServiceCollection serviceCollection)
        {
            foreach (var item in serviceCollection)
            {
                if (item.ServiceType == typeof(BasePublishManager))
                {
                    serviceCollection.Remove(item);
                    serviceCollection.Add(ServiceDescriptor.Singleton(typeof(BasePublishManager), typeof(SupportDefaultPublishManager)));

                    break;
                }
            }
        }
    }
}