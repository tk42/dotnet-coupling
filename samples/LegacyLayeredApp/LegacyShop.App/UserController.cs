using LegacyShop.Data;

namespace LegacyShop.App
{
    // 看板例の再現: 別プロジェクトの具象 SqlUserRepository を field で直接保持。
    // 期待エッジ: App.UserController --FieldType--> Data.SqlUserRepository
    //   strength 0.85 / distance 0.65(別project) → hotspot + ConcreteDependency。
    public class UserController
    {
        private readonly SqlUserRepository _repository;

        public UserController()
        {
            _repository = new SqlUserRepository();
        }

        public string GetUserName(int id)
        {
            return _repository.GetById(id).Name;
        }
    }
}
