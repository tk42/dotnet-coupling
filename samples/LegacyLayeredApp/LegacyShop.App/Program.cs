namespace LegacyShop.App
{
    public static class Program
    {
        public static void Main()
        {
            var controller = new UserController();
            System.Console.WriteLine(controller.GetUserName(1));
        }
    }
}
