using System.Collections.ObjectModel;
using BusinessOS.Pharmacy.Application.Abstractions.Administration;
using BusinessOS.Pharmacy.Application.Abstractions.Sync;
using BusinessOS.Pharmacy.Desktop.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace BusinessOS.Pharmacy.Desktop.Administration;

public sealed partial class AccessManagementViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private UiLanguage _language = UiLanguageCatalog.All[0];

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private bool isUsersMode = true;
    [ObservableProperty] private AccessUserItem? selectedUser;
    [ObservableProperty] private AccessRoleItem? selectedRole;
    [ObservableProperty] private string userName = string.Empty;
    [ObservableProperty] private string userEmail = string.Empty;
    [ObservableProperty] private string userPassword = string.Empty;
    [ObservableProperty] private bool userIsActive = true;
    [ObservableProperty] private string roleName = string.Empty;
    [ObservableProperty] private string roleCode = string.Empty;

    public AccessManagementViewModel(IServiceProvider services)
    {
        _services = services;
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        NewUserCommand = new RelayCommand(NewUser, () => !IsBusy);
        SaveUserCommand = new AsyncRelayCommand(SaveUserAsync, () => !IsBusy);
        NewRoleCommand = new RelayCommand(NewRole, () => !IsBusy);
        SaveRoleCommand = new AsyncRelayCommand(SaveRoleAsync, () => !IsBusy && SelectedRole?.Code != "owner");
    }

    public ObservableCollection<AccessUserItem> Users { get; } = new();
    public ObservableCollection<AccessRoleItem> Roles { get; } = new();
    public ObservableCollection<SelectableRoleItem> UserRoles { get; } = new();
    public ObservableCollection<SelectablePermissionItem> RolePermissions { get; } = new();

    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand NewUserCommand { get; }
    public IAsyncRelayCommand SaveUserCommand { get; }
    public IRelayCommand NewRoleCommand { get; }
    public IAsyncRelayCommand SaveRoleCommand { get; }

    public bool IsRolesMode => !IsUsersMode;
    public bool IsEditingUser => SelectedUser is not null;
    public bool IsEditingRole => SelectedRole is not null;
    public bool IsOwnerRole => string.Equals(SelectedRole?.Code, "owner", StringComparison.OrdinalIgnoreCase);

    public string Title => IsUsersMode
        ? T("Users", "کاربران", "کارنان")
        : T("Roles & Permissions", "نقش‌ها و مجوزها", "رولونه او اجازې");

    public string Subtitle => IsUsersMode
        ? T("Manage pharmacy staff accounts and assigned roles.", "حساب‌های کارمندان دواخانه و نقش‌های آنان را مدیریت کنید.", "د درملتون د کارکوونکو حسابونه او رولونه اداره کړئ.")
        : T("Control role permissions used by the desktop and web pharmacy.", "مجوزهای نقش‌ها را برای نسخه دسکتاپ و وب مدیریت کنید.", "د ډیسټاپ او ویب درملتون د رول اجازې اداره کړئ.");

    public string SaveUserLabel => IsEditingUser
        ? T("Update user", "به‌روزرسانی کاربر", "کارن تازه کړئ")
        : T("Create user", "ایجاد کاربر", "کارن جوړ کړئ");

    public string SaveRoleLabel => IsEditingRole
        ? T("Update permissions", "به‌روزرسانی مجوزها", "اجازې تازه کړئ")
        : T("Create role", "ایجاد نقش", "رول جوړ کړئ");

    public void SetLanguage(UiLanguage language)
    {
        _language = language;
        RaiseLabels();
    }

    public void SetSection(string section)
    {
        IsUsersMode = !string.Equals(section, "roles", StringComparison.OrdinalIgnoreCase);
        OnPropertyChanged(nameof(IsRolesMode));
        RaiseLabels();
    }

    public async Task LoadAsync()
    {
        var service = _services.GetService<IAccessManagementService>();
        if (service is null)
        {
            StatusMessage = T(
                "User and role management is available from the Main Server or Standalone installation.",
                "مدیریت کاربران و نقش‌ها فقط در سرور اصلی یا نصب مستقل در دسترس است.",
                "د کارن او رول مدیریت په اصلي سرور یا مستقل نصب کې شته.");
            return;
        }

        await BusyAsync(async () =>
        {
            var sync = _services.GetService<ICloudSyncService>();
            if (sync is not null)
            {
                _ = await sync.SyncOnceAsync();
            }

            var snapshot = await service.LoadAsync();
            ApplySnapshot(snapshot);
            StatusMessage = T(
                "Access management data refreshed from the pharmacy cloud.",
                "اطلاعات مدیریت دسترسی از فضای ابری دواخانه تازه شد.",
                "د لاسرسي مدیریت معلومات د درملتون له کلاوډ څخه تازه شول.");
        });
    }

    partial void OnIsUsersModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsRolesMode));
        RaiseLabels();
    }

    partial void OnSelectedUserChanged(AccessUserItem? value)
    {
        if (value is null)
        {
            OnPropertyChanged(nameof(IsEditingUser));
            OnPropertyChanged(nameof(SaveUserLabel));
            return;
        }

        UserName = value.Name;
        UserEmail = value.Email;
        UserPassword = string.Empty;
        UserIsActive = value.IsActive;
        var selectedIds = value.Roles.Select(x => x.Id).ToHashSet();
        foreach (var role in UserRoles)
            role.IsSelected = selectedIds.Contains(role.Id);

        OnPropertyChanged(nameof(IsEditingUser));
        OnPropertyChanged(nameof(SaveUserLabel));
    }

    partial void OnSelectedRoleChanged(AccessRoleItem? value)
    {
        if (value is null)
        {
            OnPropertyChanged(nameof(IsEditingRole));
            OnPropertyChanged(nameof(IsOwnerRole));
            OnPropertyChanged(nameof(SaveRoleLabel));
            SaveRoleCommand.NotifyCanExecuteChanged();
            return;
        }

        RoleName = value.Name;
        RoleCode = value.Code;
        var selectedIds = value.Permissions.Select(x => x.Id).ToHashSet();
        foreach (var permission in RolePermissions)
            permission.IsSelected = selectedIds.Contains(permission.Id);

        OnPropertyChanged(nameof(IsEditingRole));
        OnPropertyChanged(nameof(IsOwnerRole));
        OnPropertyChanged(nameof(SaveRoleLabel));
        SaveRoleCommand.NotifyCanExecuteChanged();
    }

    private void NewUser()
    {
        SelectedUser = null;
        UserName = string.Empty;
        UserEmail = string.Empty;
        UserPassword = string.Empty;
        UserIsActive = true;
        foreach (var role in UserRoles)
            role.IsSelected = false;
        OnPropertyChanged(nameof(IsEditingUser));
        OnPropertyChanged(nameof(SaveUserLabel));
    }

    private void NewRole()
    {
        SelectedRole = null;
        RoleName = string.Empty;
        RoleCode = string.Empty;
        foreach (var permission in RolePermissions)
            permission.IsSelected = false;
        OnPropertyChanged(nameof(IsEditingRole));
        OnPropertyChanged(nameof(IsOwnerRole));
        OnPropertyChanged(nameof(SaveRoleLabel));
        SaveRoleCommand.NotifyCanExecuteChanged();
    }

    private async Task SaveUserAsync()
    {
        var service = _services.GetService<IAccessManagementService>();
        if (service is null)
            return;

        if (string.IsNullOrWhiteSpace(UserName) ||
            string.IsNullOrWhiteSpace(UserEmail))
        {
            StatusMessage = T("Name and email are required.", "نام و ایمیل ضروری است.", "نوم او برېښنالیک اړین دي.");
            return;
        }

        var roleIds = UserRoles.Where(x => x.IsSelected).Select(x => x.Id).ToList();
        if (roleIds.Count == 0)
        {
            StatusMessage = T("Select at least one role.", "حداقل یک نقش را انتخاب کنید.", "لږ تر لږه یو رول وټاکئ.");
            return;
        }

        if (SelectedUser is null && UserPassword.Length < 8)
        {
            StatusMessage = T("New users require a password of at least 8 characters.", "برای کاربر جدید رمز عبور حداقل ۸ حرف لازم است.", "نوی کارن لږ تر لږه ۸ توري پټنوم غواړي.");
            return;
        }

        await BusyAsync(async () =>
        {
            var request = new SaveAccessUserRequest(
                UserName.Trim(),
                UserEmail.Trim(),
                string.IsNullOrWhiteSpace(UserPassword) ? null : UserPassword,
                UserIsActive,
                roleIds);

            var snapshot = SelectedUser is null
                ? await service.CreateUserAsync(request)
                : await service.UpdateUserAsync(SelectedUser.Id, request);

            var email = UserEmail.Trim();
            ApplySnapshot(snapshot);
            SelectedUser = Users.FirstOrDefault(x =>
                string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase));
            StatusMessage = T("User saved.", "کاربر ذخیره شد.", "کارن خوندي شو.");
        });
    }

    private async Task SaveRoleAsync()
    {
        var service = _services.GetService<IAccessManagementService>();
        if (service is null || IsOwnerRole)
            return;

        var permissionIds = RolePermissions.Where(x => x.IsSelected).Select(x => x.Id).ToList();
        if (permissionIds.Count == 0)
        {
            StatusMessage = T("Select at least one permission.", "حداقل یک مجوز را انتخاب کنید.", "لږ تر لږه یوه اجازه وټاکئ.");
            return;
        }

        await BusyAsync(async () =>
        {
            AccessManagementSnapshot snapshot;
            if (SelectedRole is null)
            {
                if (string.IsNullOrWhiteSpace(RoleName) || string.IsNullOrWhiteSpace(RoleCode))
                {
                    StatusMessage = T("Role name and code are required.", "نام و کد نقش ضروری است.", "د رول نوم او کوډ اړین دي.");
                    return;
                }

                snapshot = await service.CreateRoleAsync(
                    new SaveAccessRoleRequest(RoleName.Trim(), RoleCode.Trim(), permissionIds));
            }
            else
            {
                snapshot = await service.UpdateRoleAsync(SelectedRole.Id, permissionIds);
            }

            var roleCode = RoleCode.Trim();
            ApplySnapshot(snapshot);
            SelectedRole = Roles.FirstOrDefault(x =>
                string.Equals(x.Code, roleCode, StringComparison.OrdinalIgnoreCase));
            StatusMessage = T("Role saved.", "نقش ذخیره شد.", "رول خوندي شو.");
        });
    }

    private void ApplySnapshot(AccessManagementSnapshot snapshot)
    {
        var selectedUserId = SelectedUser?.Id;
        var selectedRoleId = SelectedRole?.Id;

        Users.Clear();
        foreach (var user in snapshot.Users)
            Users.Add(user);

        Roles.Clear();
        foreach (var role in snapshot.Roles)
            Roles.Add(role);

        UserRoles.Clear();
        foreach (var role in snapshot.Roles)
            UserRoles.Add(new SelectableRoleItem(role.Id, role.Name, role.Code));

        RolePermissions.Clear();
        foreach (var permission in snapshot.Permissions)
            RolePermissions.Add(new SelectablePermissionItem(
                permission.Id,
                permission.Code,
                permission.Name,
                permission.Description));

        SelectedUser = selectedUserId is null
            ? null
            : Users.FirstOrDefault(x => x.Id == selectedUserId);
        SelectedRole = selectedRoleId is null
            ? null
            : Roles.FirstOrDefault(x => x.Id == selectedRoleId);
    }

    private async Task BusyAsync(Func<Task> action)
    {
        if (IsBusy)
            return;

        IsBusy = true;
        NotifyCommands();
        try
        {
            await action();
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
            NotifyCommands();
        }
    }

    private void NotifyCommands()
    {
        RefreshCommand.NotifyCanExecuteChanged();
        NewUserCommand.NotifyCanExecuteChanged();
        SaveUserCommand.NotifyCanExecuteChanged();
        NewRoleCommand.NotifyCanExecuteChanged();
        SaveRoleCommand.NotifyCanExecuteChanged();
    }

    private void RaiseLabels()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(SaveUserLabel));
        OnPropertyChanged(nameof(SaveRoleLabel));
    }

    private string T(string english, string dari, string pashto) =>
        _language.Code switch
        {
            "fa" => dari,
            "ps" => pashto,
            _ => english,
        };
}

public sealed partial class SelectableRoleItem : ObservableObject
{
    public SelectableRoleItem(int id, string name, string code)
    {
        Id = id;
        Name = name;
        Code = code;
    }

    public int Id { get; }
    public string Name { get; }
    public string Code { get; }
    [ObservableProperty] private bool isSelected;
}

public sealed partial class SelectablePermissionItem : ObservableObject
{
    public SelectablePermissionItem(int id, string code, string name, string? description)
    {
        Id = id;
        Code = code;
        Name = name;
        Description = description;
    }

    public int Id { get; }
    public string Code { get; }
    public string Name { get; }
    public string? Description { get; }
    [ObservableProperty] private bool isSelected;
}
