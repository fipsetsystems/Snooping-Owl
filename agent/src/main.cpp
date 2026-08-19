#include "bootstrap.h"
#include "diagnostics/file_logger.h"

#include <QCommandLineParser>
#include <QCoreApplication>
#include <QDebug>

#ifdef Q_OS_WIN
#include "service/service_core.h"
#include "service/service_manager.h"
#endif

#ifndef AGENT_VERSION
#define AGENT_VERSION "dev"
#endif

int main(int argc, char* argv[])
{
#ifdef Q_OS_WIN
    // The Service Control Manager launches the binary without arguments. In
    // that case no QCoreApplication exists yet: the service constructs its
    // own Qt application on the SCM service thread (see service_core.cpp).
    if (argc <= 1) {
        return service::runService();
    }
#endif

    QCoreApplication app(argc, argv);
    QCoreApplication::setApplicationName(QStringLiteral("SnoopingOwl"));
    QCoreApplication::setApplicationVersion(QStringLiteral(AGENT_VERSION));
    QCoreApplication::setOrganizationName(QStringLiteral("SnoopingOwl"));

    QCommandLineParser parser;
    parser.setApplicationDescription(
        QStringLiteral("SnoopingOwl workstation operations agent."));
    parser.addHelpOption();
    parser.addVersionOption();

#ifdef Q_OS_WIN
    QCommandLineOption installOption(
        QStringList{ QStringLiteral("i"), QStringLiteral("install") },
        QStringLiteral("Install and start the SnoopingOwl Windows service "
                       "(requires administrator)."));
    QCommandLineOption uninstallOption(
        QStringList{ QStringLiteral("u"), QStringLiteral("uninstall") },
        QStringLiteral("Stop and remove the SnoopingOwl Windows service "
                       "(requires administrator)."));
    parser.addOption(installOption);
    parser.addOption(uninstallOption);
#endif

    QCommandLineOption runOption(
        QStringList{ QStringLiteral("r"), QStringLiteral("run") },
        QStringLiteral("Run the agent in the foreground without registering "
                       "a service."));
    parser.addOption(runOption);

    parser.process(app);

#ifdef Q_OS_WIN
    if (parser.isSet(installOption)) {
        return service::installService();
    }
    if (parser.isSet(uninstallOption)) {
        return service::uninstallService();
    }
#endif

    if (!agent::initializeFoundation(parser.isSet(runOption))) {
        qCritical("Agent startup aborted: configuration could not be loaded");
        return 1;
    }

    qInfo("SnoopingOwl agent started (foreground, version %s)", AGENT_VERSION);

    QObject::connect(&app, &QCoreApplication::aboutToQuit, [] {
        qInfo("SnoopingOwl agent stopped");
        diagnostics::shutdownLogging();
    });

    return app.exec();
}