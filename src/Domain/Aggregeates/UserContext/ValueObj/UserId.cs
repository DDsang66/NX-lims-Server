using NX_lims_Softlines_Command_System.src.Domain.Share;

namespace NX_lims_Softlines_Command_System.src.Domain.Aggregeates.UserContext.ValueObj
{
    public class UserId:AggregateRootId<Guid>
    {
        // 显式构造函数，接收 string 类型的值，并传给基类
        public UserId(Guid value)
            : base(value)
        {
            if (value == Guid.Empty)
                throw new ArgumentNullException(nameof(value));
        }
    }
}
