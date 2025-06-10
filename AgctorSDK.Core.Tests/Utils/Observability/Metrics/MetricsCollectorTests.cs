using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using AgctorSDK.Core.Utils.Observability.Metrics;
using AgctorSDK.Core.Interfaces;
using AgctorSDK.Core.Messages;
using Moq;

namespace AgctorSDK.Core.Tests.Utils.Observability.Metrics
{
    public class MetricsCollectorTests
    {
        [Fact]
        public void IncrementCounter_ShouldNotThrow()
        {
            // Arrange
            var collector = new OpenTelemetryMetricsCollector();
            
            // Act & Assert
            var exception = Record.Exception(() => collector.IncrementCounter("test_counter"));
            Assert.Null(exception);
        }
        
        [Fact]
        public void IncrementCounter_WithTags_ShouldNotThrow()
        {
            // Arrange
            var collector = new OpenTelemetryMetricsCollector();
            
            // Act & Assert
            var exception = Record.Exception(() => collector.IncrementCounter(
                "test_counter", 
                1, 
                new KeyValuePair<string, object>("tag1", "value1"),
                new KeyValuePair<string, object>("tag2", "value2")));
                
            Assert.Null(exception);
        }
        
        [Fact]
        public void RecordGauge_ShouldNotThrow()
        {
            // Arrange
            var collector = new OpenTelemetryMetricsCollector();
            
            // Act & Assert
            var exception = Record.Exception(() => collector.RecordGauge("test_gauge", 42.0));
            Assert.Null(exception);
        }
        
        [Fact]
        public void RecordHistogram_ShouldNotThrow()
        {
            // Arrange
            var collector = new OpenTelemetryMetricsCollector();
            
            // Act & Assert
            var exception = Record.Exception(() => collector.RecordHistogram("test_histogram", 100.0));
            Assert.Null(exception);
        }
        
        [Fact]
        public async Task TimeOperation_ShouldMeasureDuration()
        {
            // Arrange
            var collector = new OpenTelemetryMetricsCollector();
            
            // Act & Assert - just verify it doesn't throw
            using (var timer = collector.TimeOperation("test_timer"))
            {
                // Simulate some work
                await Task.Delay(10);
            }
        }
        
        [Fact]
        public void NoOpMetricsCollector_ShouldNotThrow()
        {
            // Arrange
            var collector = NoOpMetricsCollector.Instance;
            
            // Act & Assert
            var exception = Record.Exception(() => 
            {
                collector.IncrementCounter("test_counter");
                collector.RecordGauge("test_gauge", 42.0);
                collector.RecordHistogram("test_histogram", 100.0);
                using (var timer = collector.TimeOperation("test_timer"))
                {
                    // Do nothing
                }
            });
            
            Assert.Null(exception);
        }
        
        [Fact]
        public async Task MetricsEnabledActor_ShouldCollectMetrics()
        {
            // Arrange
            var mockInnerActor = new Mock<IActor>();
            mockInnerActor.Setup(a => a.Id).Returns("test-actor-id");
            mockInnerActor.Setup(a => a.ActorType).Returns("TestActor");
            mockInnerActor.Setup(a => a.ReceiveAsync(It.IsAny<IMessageEnvelope>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new MessageEnvelope("response"));
            
            var mockEnvelope = new Mock<IMessageEnvelope>();
            mockEnvelope.Setup(m => m.Payload).Returns(new TestMessage());
            
            var collector = new OpenTelemetryMetricsCollector();
            
            // Act
            var metricsActor = new MetricsEnabledActor(mockInnerActor.Object, collector);
            await metricsActor.ReceiveAsync(mockEnvelope.Object);
            await metricsActor.ShutdownAsync();
            
            // Assert - we're just verifying it doesn't throw exceptions
            mockInnerActor.Verify(a => a.ReceiveAsync(mockEnvelope.Object, It.IsAny<CancellationToken>()), Times.Once);
            mockInnerActor.Verify(a => a.ShutdownAsync(It.IsAny<CancellationToken>()), Times.Once);
        }
        
        [Fact]
        public void DI_Registration_ShouldRegisterMetricsCollector()
        {
            // Arrange
            var services = new ServiceCollection();
            
            // Act
            services.AddAgctorMetrics();
            var serviceProvider = services.BuildServiceProvider();
            
            // Assert
            var collector = serviceProvider.GetService<IMetricsCollector>();
            Assert.NotNull(collector);
            Assert.IsType<OpenTelemetryMetricsCollector>(collector);
        }
        
        [Fact]
        public void DI_Registration_ShouldRegisterNoOpMetricsCollector()
        {
            // Arrange
            var services = new ServiceCollection();
            
            // Act
            services.AddAgctorNoOpMetrics();
            var serviceProvider = services.BuildServiceProvider();
            
            // Assert
            var collector = serviceProvider.GetService<IMetricsCollector>();
            Assert.NotNull(collector);
            Assert.Same(NoOpMetricsCollector.Instance, collector);
        }
        
        private class TestMessage { }
    }
} 