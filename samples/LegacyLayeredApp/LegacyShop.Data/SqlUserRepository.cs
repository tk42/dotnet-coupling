namespace LegacyShop.Data
{
    // 具象実装。上位層がこれに直接依存すると ConcreteDependency / LayerViolation の対象になる。
    public class SqlUserRepository : IUserRepository
    {
        public User GetById(int id)
        {
            return new User { Id = id, Name = "sample" };
        }
    }
}
