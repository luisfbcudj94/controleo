using Android.Content.Res;
using Google.Android.Material.BottomNavigation;
using Microsoft.Maui.Controls.Handlers;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace Controleo.Mobile.Platforms.Android;

public class CustomTabbedPageHandler : TabbedViewHandler
{
    protected override void ConnectHandler(global::Android.Views.View platformView)
    {
        base.ConnectHandler(platformView);
        ConfigureBottomNav(platformView);
    }

    private static void ConfigureBottomNav(global::Android.Views.View view)
    {
        var bottomNav = FindBottomNavigationView(view);
        if (bottomNav is null) return;

        // Allow 6+ items via reflection (MaxItemCount is read-only property)
        try
        {
            var menu = bottomNav.Menu;
            var maxMethod = bottomNav.Class.GetMethod("setMaxItemCount", Java.Lang.Class.FromType(typeof(int)));
            maxMethod?.Invoke(bottomNav, new Java.Lang.Integer(6));
        }
        catch
        {
            // Fallback: not all Material versions support this
        }

        // Disable icon tinting so colored icons show as-is
        bottomNav.ItemIconTintList = null;
    }

    private static BottomNavigationView? FindBottomNavigationView(global::Android.Views.View view)
    {
        if (view is BottomNavigationView bnv) return bnv;
        if (view is global::Android.Views.ViewGroup vg)
        {
            for (int i = 0; i < vg.ChildCount; i++)
            {
                var child = vg.GetChildAt(i);
                if (child is null) continue;
                var found = FindBottomNavigationView(child);
                if (found is not null) return found;
            }
        }
        return null;
    }
}
