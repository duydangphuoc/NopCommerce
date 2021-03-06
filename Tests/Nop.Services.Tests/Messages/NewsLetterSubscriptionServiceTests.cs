using Nop.Core.Data;
using Nop.Core.Domain.Customers;
using Nop.Core.Domain.Messages;
using Nop.Data;
using Nop.Services.Customers;
using Nop.Services.Events;
using Nop.Services.Messages;
using NUnit.Framework;
using Rhino.Mocks;

namespace Nop.Services.Tests.Messages 
{
    [TestFixture]
    public class NewsLetterSubscriptionServiceTests : ServiceTest
    {
        private IEventPublisher _eventPublisher;
        private IRepository<NewsLetterSubscription> _newsLetterSubscriptionRepository;
        private IRepository<Customer> _customerRepository;
        private ICustomerService _customerService;
        private IDbContext _dbContext;

        [SetUp]
        public new void SetUp()
        {
            _eventPublisher = MockRepository.GenerateStub<IEventPublisher>();
            _newsLetterSubscriptionRepository = MockRepository.GenerateMock<IRepository<NewsLetterSubscription>>();
            _customerRepository = MockRepository.GenerateMock<IRepository<Customer>>();
            _customerService = MockRepository.GenerateMock<ICustomerService>();
            _dbContext = MockRepository.GenerateStub<IDbContext>();
        }

        /// <summary>
        /// Verifies the active insert triggers subscribe event.
        /// </summary>
        [Test]
        public void VerifyActiveInsertTriggersSubscribeEvent()
        {
            var service = new NewsLetterSubscriptionService(_dbContext, _newsLetterSubscriptionRepository,
                _customerRepository, _eventPublisher, _customerService);

            var subscription = new NewsLetterSubscription { Active = true, Email = "test@test.com" };
            service.InsertNewsLetterSubscription(subscription, true);

            _eventPublisher.AssertWasCalled(x => x.Publish(new EmailSubscribedEvent(subscription)));
        }

        /// <summary>
        /// Verifies the delete triggers unsubscribe event.
        /// </summary>
        [Test]
        public void VerifyDeleteTriggersUnsubscribeEvent()
        {
            var service = new NewsLetterSubscriptionService(_dbContext, _newsLetterSubscriptionRepository,
                _customerRepository, _eventPublisher, _customerService);

            var subscription = new NewsLetterSubscription { Active = true, Email = "test@test.com" };
            service.DeleteNewsLetterSubscription(subscription, true);

            _eventPublisher.AssertWasCalled(x => x.Publish(new EmailUnsubscribedEvent(subscription)));
        }
        
        /// <summary>
        /// Verifies the insert event is fired.
        /// </summary>
        [Test]
        public void VerifyInsertEventIsFired()
        {
            var service = new NewsLetterSubscriptionService(_dbContext, _newsLetterSubscriptionRepository,
                _customerRepository, _eventPublisher, _customerService);

            service.InsertNewsLetterSubscription(new NewsLetterSubscription { Email = "test@test.com" });

            _eventPublisher.AssertWasCalled(x => x.EntityInserted(Arg<NewsLetterSubscription>.Is.Anything));
        }
    }
}