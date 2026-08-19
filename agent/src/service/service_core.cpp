#include "service/service_core.h"

#include "bootstrap.h"
#include "diagnostics/file_logger.h"

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>

#include <QCoreApplication>
#include <QMetaObject>

namespace {

constexpr wchar_t kServiceName[] = L"SnoopingOwl";

// Service lifecycle is driven by the SCM on its own thread while the Qt
// event loop runs on the service thread (the thread the SCM created for
// ServiceMain). Control requests are forwarded to the Qt loop so shutdown
// is clean and cooperative.
SERVICE_STATUS g_status{};
SERVICE_STATUS_HANDLE g_statusHandle = nullptr;
std::atomic<QCoreApplication*> g_application = nullptr;

// Controls accepted once the service reports SERVICE_RUNNING.
constexpr DWORD kAcceptedControls = SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN;

void reportStatus(DWORD state, DWORD exitCode = NO_ERROR, DWORD waitHint = 0)
{
    g_status.dwServiceType = SERVICE_WIN32_OWN_PROCESS;
    g_status.dwCurrentState = state;
    // Pending states must not accept controls; the SCM refuses them.
    g_status.dwControlsAccepted =
        (state == SERVICE_RUNNING) ? kAcceptedControls : 0;
    g_status.dwWin32ExitCode = exitCode;
    g_status.dwWaitHint = waitHint;
    g_status.dwCheckPoint = (state == SERVICE_RUNNING) ? 0 : 1;
    SetServiceStatus(g_statusHandle, &g_status);
}

DWORD WINAPI serviceControlHandler(DWORD control, DWORD, LPVOID, LPVOID)
{
    switch (control) {
    case SERVICE_CONTROL_STOP:
    case SERVICE_CONTROL_SHUTDOWN:
        reportStatus(SERVICE_STOP_PENDING, NO_ERROR, 5000);
        if (QCoreApplication* app = g_application.load()) {
            QMetaObject::invokeMethod(
                app, [app] { app->quit(); }, Qt::QueuedConnection);
        }
        break;
    default:
        // Unaccepted controls are ignored; the SCM only sends accepted ones.
        break;
    }
    return NO_ERROR;
}

void WINAPI serviceMain(DWORD /*argc*/, LPWSTR* /*argv*/)
{
    g_statusHandle = RegisterServiceCtrlHandlerExW(
        kServiceName, &serviceControlHandler, nullptr);
    if (g_statusHandle == nullptr) {
        return;
    }

    reportStatus(SERVICE_START_PENDING, NO_ERROR, 5000);

    if (!agent::initializeFoundation(/*mirrorToStderr=*/false)) {
        reportStatus(SERVICE_STOPPED, ERROR_SERVICE_SPECIFIC_ERROR);
        return;
    }

    // The Qt application lives on this (service) thread; its event loop is
    // the agent's main loop. QCoreApplication has no GUI requirements, so
    // running it on the SCM service thread is safe.
    char emptyArgv[] = "";
    char* argvPtr[] = { emptyArgv, nullptr };
    int qtArgc = 1;
    QCoreApplication app(qtArgc, argvPtr);

    g_application.store(&app);
    reportStatus(SERVICE_RUNNING);
    qInfo("SnoopingOwl agent service running");

    app.exec();

    qInfo("SnoopingOwl agent service stopped");
    g_application.store(nullptr);
    diagnostics::shutdownLogging();
    reportStatus(SERVICE_STOPPED);
}

} // namespace

namespace service {

int runService()
{
    const SERVICE_TABLE_ENTRYW table[] = {
        { const_cast<LPWSTR>(kServiceName), &serviceMain },
        { nullptr, nullptr },
    };

    if (!StartServiceCtrlDispatcherW(table)) {
        return 1;
    }
    return 0;
}

} // namespace service