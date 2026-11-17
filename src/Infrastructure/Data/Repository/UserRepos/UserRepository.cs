using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.UserContext;
using NX_lims_Softlines_Command_System.src.Domain.Aggregeates.UserContext.ValueObj;
using NX_lims_Softlines_Command_System.src.Domain.Contract.Repository;
using NX_lims_Softlines_Command_System.src.Domain.Share.DependencyInject;
using NX_lims_Softlines_Command_System.src.Infrastructure.Data.Persistence;

namespace NX_lims_Softlines_Command_System.src.Infrastructure.Data.Repository.UserRepos
{
    public class UserRepository:IScopedDependency, IUserRepository
    {
        private readonly dbContext _dbContext;
        public UserRepository(dbContext dbContext)
        {
            _dbContext = dbContext;
        }
        public Task<User?> FindByUserNameAsync(string userName, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task<User?> GetByIdAsync(UserId id, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task<User?> FindByIdentifierAsync(string identifier, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task<bool> ExistsByUserNameAsync(string userName, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task AddAsync(User user, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task UpdatePasswordAsync(UserId id, string passwordHash, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task UpdateAsync(User user, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
        public Task ResetLoginFailureAsync(UserId id, string? lastLoginIp, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task<int> RegisterLoginFailureAsync(UserId id, int lockThreshold, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

    }
}
