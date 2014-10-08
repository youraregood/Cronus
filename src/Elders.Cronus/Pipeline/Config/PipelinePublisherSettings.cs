using System;
using Elders.Cronus.DomainModeling;
using Elders.Cronus.IocContainer;
using Elders.Cronus.Pipeline.Transport;
using Elders.Cronus.Serializer;

namespace Elders.Cronus.Pipeline.Config
{
    public abstract class PipelinePublisherSettings<TContract> : SettingsBuilder, IPipelinePublisherSettings<TContract> where TContract : IMessage
    {
        public PipelinePublisherSettings(ISettingsBuilder settingsBuilder) : base(settingsBuilder) { }

        public override void Build()
        {
            var builder = this as ISettingsBuilder;
            Func<IPipelineTransport> transport = () => builder.Container.Resolve<IPipelineTransport>(builder.Name);
            Func<ISerializer> serializer = () => builder.Container.Resolve<ISerializer>();
            builder.Container.RegisterSingleton<IPublisher<TContract>>(() =>
                new PipelinePublisher<TContract>(transport(), serializer()), builder.Name);
        }
    }

    public class CommandPipelinePublisherSettings : PipelinePublisherSettings<ICommand>
    {
        public CommandPipelinePublisherSettings(ISettingsBuilder settingsBuilder) : base(settingsBuilder) { }
    }

    public class EventPipelinePublisherSettings : PipelinePublisherSettings<IEvent>
    {
        public EventPipelinePublisherSettings(ISettingsBuilder settingsBuilder) : base(settingsBuilder) { }
    }
}