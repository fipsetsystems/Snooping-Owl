#include "service/service_manager.h"

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <winsvc.h>
#include <sddl.h>

#include <QCoreApplication>
#include <QDebug>
#include <QDir>

namespace {

constexpr wchar_t kServiceName[] = L"SnoopingOwl";
constexpr wchar_t kDisplayName[] = L"SnoopingOwl Agent";
constexpr wchar_t kDescription[] =
    L"SnoopingOwl workstation operations agent.";

// Service DACL restricted to SYSTEM and Builtin Administrators so ordinary
// users cannot stop, start, or reconfigure the agent.
constexpr wchar_t kServiceDacl[] =
    L"D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)"
    L"(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)";

// Restart on failure, like the installer's ServiceConfig will do.
constexpr int kFailureRestartDelayMs = 5000;
constexpr int kFailureResetPeriodSeconds = 86400;

QString systemErrorMessage(DWORD code)
{
    wchar_t* buffer = nullptr;
    const DWORD length = FormatMessageW(
        FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM |
            FORMAT_MESSAGE_IGNORE_INSERTS,
        nullptr, code, 0, reinterpret_cast<LPWSTR>(&buffer), 0, nullptr);
    const QString message = length > 0
        ? QString::fromWCharArray(buffer, static_cast<int>(length)).trimmed()
        : QStringLiteral("unknown error");
    LocalFree(buffer);
    return message;
}

void reportFailure(const char* what, DWORD code)
{
    qCritical("%s failed: %s", what,
              qPrintable(systemErrorMessage(code)));
}

void configureService(SC_HANDLE service)
{
    SERVICE_DESCRIPTIONW description{};
    description.lpDescription = const_cast<LPWSTR>(kDescription);
    ChangeServiceConfig2W(service, SERVICE_CONFIG_DESCRIPTION, &description);

    SC_ACTION actions[3] = {
        { SC_ACTION_RESTART, kFailureRestartDelayMs },
        { SC_ACTION_RESTART, kFailureRestartDelayMs },
        { SC_ACTION_RESTART, kFailureRestartDelayMs },
    };
    SERVICE_FAILURE_ACTIONSW failure{};
    failure.dwResetPeriod = kFailureResetPeriodSeconds;
    failure.lpRebootMsg = nullptr;
    failure.lpCommand = nullptr;
    failure.cActions = 3;
    failure.lpsaActions = actions;
    ChangeServiceConfig2W(service, SERVICE_CONFIG_FAILURE_ACTIONS, &failure);

    PSECURITY_DESCRIPTOR descriptor = nullptr;
    if (ConvertStringSecurityDescriptorToSecurityDescriptorW(
            kServiceDacl, SDDL_REVISION_1, &descriptor, nullptr)) {
        SetServiceObjectSecurity(service, DACL_SECURITY_INFORMATION,
                                 descriptor);
        LocalFree(descriptor);
    }
}

bool startService(SC_HANDLE service)
{
    if (StartServiceW(service, 0, nullptr)) {
        return true;
    }
    const DWORD code = GetLastError();
    if (code == ERROR_SERVICE_ALREADY_RUNNING) {
        qWarning("SnoopingOwl service is already running");
        return true;
    }
    reportFailure("starting SnoopingOwl service", code);
    return false;
}

} // namespace

namespace service {

int installService()
{
    const QString exePath = QDir::toNativeSeparators(
        QCoreApplication::applicationFilePath());
    const QString binPath = QStringLiteral("\"%1\"").arg(exePath);

    SC_HANDLE manager = OpenSCManagerW(
        nullptr, nullptr, SC_MANAGER_ALL_ACCESS);
    if (manager == nullptr) {
        reportFailure("opening the service control manager", GetLastError());
        return 1;
    }

    SC_HANDLE service = CreateServiceW(
        manager, kServiceName, kDisplayName, SERVICE_ALL_ACCESS,
        SERVICE_WIN32_OWN_PROCESS, SERVICE_AUTO_START, SERVICE_ERROR_NORMAL,
        reinterpret_cast<const wchar_t*>(binPath.utf16()), nullptr, nullptr,
        nullptr, nullptr, nullptr);

    if (service == nullptr) {
        const DWORD code = GetLastError();
        if (code != ERROR_SERVICE_EXISTS) {
            reportFailure("creating SnoopingOwl service", code);
            CloseServiceHandle(manager);
            return 1;
        }
        service = OpenServiceW(manager, kServiceName, SERVICE_ALL_ACCESS);
        if (service == nullptr) {
            reportFailure("opening existing SnoopingOwl service",
                          GetLastError());
            CloseServiceHandle(manager);
            return 1;
        }
        qWarning("SnoopingOwl service already exists; reconfiguring");
    } else {
        qInfo("SnoopingOwl service registered: %s", qPrintable(exePath));
    }

    configureService(service);

    const bool started = startService(service);
    CloseServiceHandle(service);
    CloseServiceHandle(manager);

    return started ? 0 : 1;
}

int uninstallService()
{
    SC_HANDLE manager = OpenSCManagerW(
        nullptr, nullptr, SC_MANAGER_ALL_ACCESS);
    if (manager == nullptr) {
        reportFailure("opening the service control manager", GetLastError());
        return 1;
    }

    SC_HANDLE service = OpenServiceW(manager, kServiceName, SERVICE_ALL_ACCESS);
    if (service == nullptr) {
        const DWORD code = GetLastError();
        if (code == ERROR_SERVICE_DOES_NOT_EXIST) {
            qWarning("SnoopingOwl service is not installed");
            CloseServiceHandle(manager);
            return 0;
        }
        reportFailure("opening SnoopingOwl service", code);
        CloseServiceHandle(manager);
        return 1;
    }

    SERVICE_STATUS status{};
    if (QueryServiceStatus(service, &status)
        && status.dwCurrentState != SERVICE_STOPPED) {
        if (!ControlService(service, SERVICE_CONTROL_STOP, &status)) {
            reportFailure("stopping SnoopingOwl service", GetLastError());
        }
        // The service signals the SCM when it has actually stopped; wait
        // for that before deleting it.
        constexpr int kStopTimeoutMs = 30000;
        constexpr int kPollIntervalMs = 500;
        for (int waited = 0; waited < kStopTimeoutMs; waited += kPollIntervalMs) {
            if (!QueryServiceStatus(service, &status)
                || status.dwCurrentState == SERVICE_STOPPED) {
                break;
            }
            Sleep(kPollIntervalMs);
        }
    }

    if (DeleteService(service)) {
        qInfo("SnoopingOwl service removed");
    } else {
        reportFailure("removing SnoopingOwl service", GetLastError());
        CloseServiceHandle(service);
        CloseServiceHandle(manager);
        return 1;
    }

    CloseServiceHandle(service);
    CloseServiceHandle(manager);
    return 0;
}

} // namespace service