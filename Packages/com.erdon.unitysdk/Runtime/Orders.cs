using System;
namespace ErDon.UnitySDK
{
    public enum OrderState { Created, Paid, Fulfilled }
    public sealed class GameOrder
    {
        public string OrderId { get; private set; }
        public string ProductId { get; private set; }
        public long AmountMinor { get; private set; }
        public string Currency { get; private set; }
        public OrderState State { get; private set; }
        public GameOrder(string orderId, string productId, long amountMinor, string currency, OrderState state)
        { OrderId=orderId;ProductId=productId;AmountMinor=amountMinor;Currency=currency;State=state; }
    }
    public sealed class OrderResult
    {
        // Success means the HTTP operation succeeded; inspect Order.State before granting any entitlement.
        public bool Success { get { return Error == null; } }
        public GameOrder Order { get; private set; }
        public SdkError Error { get; private set; }
        public string ServerCallId { get; private set; }
        public static OrderResult Ok(GameOrder order) { if(order==null)throw new ArgumentNullException("order");return new OrderResult{Order=order}; }
        public static OrderResult Fail(SdkError error) { if(error==null)throw new ArgumentNullException("error");return new OrderResult{Error=error}; }
        public static OrderResult Fail(string code,string reason) { return Fail(new SdkError(code,reason)); }
        public OrderResult WithServerCallId(string value)
        {
            Guid parsed;ServerCallId=Guid.TryParseExact(value,"D",out parsed)?parsed.ToString("D"):null;
            if(Error!=null)Error.ServerCallId=ServerCallId;return this;
        }
    }
    public interface IOrderClient : IDisposable
    {
        void Create(string session, string productId, string requestId, Action<OrderResult> callback);
        void Get(string session, string orderId, Action<OrderResult> callback);
        void Confirm(string session, string orderId, string evidence, Action<OrderResult> callback);
    }
}
