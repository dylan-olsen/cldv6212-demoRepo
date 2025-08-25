using retailMvcDemo.Services;

namespace retailMvcDemo
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 1) Register MVC + your services (ALL before Build)
            builder.Services.AddControllersWithViews();

            builder.Services.AddSingleton<CustomerTableService>();
            builder.Services.AddSingleton<ProductTableService>();
            builder.Services.AddSingleton<OrderTableService>(); // add when you build Orders

            // Register the blob service via its interface
            builder.Services.AddSingleton<IBlobService, BlobService>();

            // registering the queue service
            builder.Services.AddSingleton<IQueueService, QueueService>();


            // 2) Build
            var app = builder.Build();

            // 3) Pipeline
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Home/Error");
                app.UseHsts();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthorization();

            // 4) Routes
            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Home}/{action=Index}/{id?}");

            // 5) Run
            app.Run();
        }
    }
}
