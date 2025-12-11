using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using DiseaseMutationsApp;
using DiseaseMutationsApp.Services;
using Refit;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped(refit => RestService.For<IDiseaseMutationsApi>("http://localhost:5000"));

// Register state management service to persist data across page navigations
builder.Services.AddScoped<AppStateService>();

await builder.Build().RunAsync();