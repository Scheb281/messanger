namespace Messanger.Classes
{
    public class User
    {
        public int Id { get; set; }
        public string Nik { get; set; }
        public string Login { get; set; }
        public string Password { get; set; }
        public string Chats { get; set; } = string.Empty;
        public string[] Messages { get; set; }
    }
}
