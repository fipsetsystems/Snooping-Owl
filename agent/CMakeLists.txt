cmake_minimum_required(VERSION 3.16)

project(SnoopingOwlAgent VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_AUTOMOC ON)

find_package(Qt6 REQUIRED COMPONENTS Core Network WebSockets)
qt_standard_project_setup()

set(AGENT_SOURCES
    src/main.cpp
    src/bootstrap.cpp
    src/configuration/configuration.cpp
    src/diagnostics/file_logger.cpp
)

if(WIN32)
    list(APPEND AGENT_SOURCES
        src/service/service_core.cpp
        src/service/service_manager.cpp
    )
endif()

add_executable(agent ${AGENT_SOURCES})

if(WIN32)
    set_target_properties(agent PROPERTIES
        WIN32_EXECUTABLE TRUE
    )
    target_compile_definitions(agent PRIVATE UNICODE _UNICODE)
endif()

target_compile_definitions(agent PRIVATE AGENT_VERSION="${PROJECT_VERSION}")

target_include_directories(agent PRIVATE src)

target_link_libraries(agent PRIVATE Qt6::Core Qt6::Network Qt6::WebSockets)

qt_finalize_executable(agent)