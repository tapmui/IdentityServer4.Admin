﻿using System;
using System.Reflection;
using IdentityModel;
using IdentityServer4.AccessTokenValidation;
using IdentityServer4.EntityFramework.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Serilog;
using Skoruba.AuditLogging.EntityFramework.DbContexts;
using Skoruba.AuditLogging.EntityFramework.Entities;
using Skoruba.AuditLogging.EntityFramework.Extensions;
using Skoruba.AuditLogging.EntityFramework.Repositories;
using Skoruba.AuditLogging.EntityFramework.Services;
using Skoruba.AuditLogging.Events.Http;
using Skoruba.IdentityServer4.Admin.Api.AuditLogging;
using Skoruba.IdentityServer4.Admin.Api.Configuration;
using Skoruba.IdentityServer4.Admin.Api.Configuration.ApplicationParts;
using Skoruba.IdentityServer4.Admin.Api.Configuration.Constants;
using Skoruba.IdentityServer4.Admin.Api.Helpers.Localization;
using Skoruba.IdentityServer4.Admin.BusinessLogic.Identity.Dtos.Identity;
using Skoruba.IdentityServer4.Admin.EntityFramework.Interfaces;

namespace Skoruba.IdentityServer4.Admin.Api.Helpers
{
	public static class StartupHelpers
	{
		public static bool PostgresInUse { get; set; } = false;

		public static IServiceCollection AddAuditEventLogging<TAuditLoggingDbContext, TAuditLog>(
			this IServiceCollection services, IConfiguration configuration)
			where TAuditLog : AuditLog, new()
			where TAuditLoggingDbContext : IAuditLoggingDbContext<TAuditLog>
		{
			var auditLoggingConfiguration = configuration.GetSection(nameof(AuditLoggingConfiguration))
				.Get<AuditLoggingConfiguration>();
			services.AddSingleton(auditLoggingConfiguration);

			services.AddAuditLogging(options => { options.Source = auditLoggingConfiguration.Source; })
				.AddEventData<ApiAuditSubject, ApiAuditAction>()
				.AddAuditSinks<DatabaseAuditEventLoggerSink<TAuditLog>>();

			services
				.AddTransient<IAuditLoggingRepository<TAuditLog>,
					AuditLoggingRepository<TAuditLoggingDbContext, TAuditLog>>();

			return services;
		}

		/// <summary>
		/// Register services for MVC
		/// </summary>
		/// <param name="services"></param>
		public static void AddMvcServices<TUserDto, TUserDtoKey, TRoleDto, TRoleDtoKey, TUserKey, TRoleKey,
			TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken,
			TUsersDto, TRolesDto, TUserRolesDto, TUserClaimsDto,
			TUserProviderDto, TUserProvidersDto, TUserChangePasswordDto, TRoleClaimsDto>(
			this IServiceCollection services)
			where TUserDto : UserDto<TUserDtoKey>, new()
			where TRoleDto : RoleDto<TRoleDtoKey>, new()
			where TUser : IdentityUser<TKey>
			where TRole : IdentityRole<TKey>
			where TKey : IEquatable<TKey>
			where TUserClaim : IdentityUserClaim<TKey>
			where TUserRole : IdentityUserRole<TKey>
			where TUserLogin : IdentityUserLogin<TKey>
			where TRoleClaim : IdentityRoleClaim<TKey>
			where TUserToken : IdentityUserToken<TKey>
			where TRoleDtoKey : IEquatable<TRoleDtoKey>
			where TUserDtoKey : IEquatable<TUserDtoKey>
			where TUsersDto : UsersDto<TUserDto, TUserDtoKey>
			where TRolesDto : RolesDto<TRoleDto, TRoleDtoKey>
			where TUserRolesDto : UserRolesDto<TRoleDto, TUserDtoKey, TRoleDtoKey>
			where TUserClaimsDto : UserClaimsDto<TUserDtoKey>
			where TUserProviderDto : UserProviderDto<TUserDtoKey>
			where TUserProvidersDto : UserProvidersDto<TUserDtoKey>
			where TUserChangePasswordDto : UserChangePasswordDto<TUserDtoKey>
			where TRoleClaimsDto : RoleClaimsDto<TRoleDtoKey>
		{
			services.TryAddTransient(typeof(IGenericControllerLocalizer<>), typeof(GenericControllerLocalizer<>));

			services.AddControllersWithViews(o => { o.Conventions.Add(new GenericControllerRouteConvention()); })
				.AddDataAnnotationsLocalization()
				.ConfigureApplicationPartManager(m =>
				{
					m.FeatureProviders.Add(
						new GenericTypeControllerFeatureProvider<TUserDto, TUserDtoKey, TRoleDto, TRoleDtoKey, TUserKey,
							TRoleKey, TUser, TRole, TKey, TUserClaim, TUserRole, TUserLogin, TRoleClaim, TUserToken,
							TUsersDto, TRolesDto, TUserRolesDto, TUserClaimsDto,
							TUserProviderDto, TUserProvidersDto, TUserChangePasswordDto, TRoleClaimsDto>());
				});
		}

		/// <summary>
		/// Add configuration for logging
		/// </summary>
		/// <param name="app"></param>
		/// <param name="configuration"></param>
		public static void AddLogging(this IApplicationBuilder app, IConfiguration configuration)
		{
			Log.Logger = new LoggerConfiguration()
				.ReadFrom.Configuration(configuration)
				.CreateLogger();
		}

		/// <summary>
		/// Register DbContexts for IdentityServer ConfigurationStore and PersistedGrants, Identity and Logging
		/// Configure the connection strings in AppSettings.json
		/// </summary>
		/// <typeparam name="TConfigurationDbContext"></typeparam>
		/// <typeparam name="TPersistedGrantDbContext"></typeparam>
		/// <typeparam name="TLogDbContext"></typeparam>
		/// <typeparam name="TIdentityDbContext"></typeparam>
		/// <param name="services"></param>
		/// <param name="configuration"></param>
		public static void AddDbContexts<TIdentityDbContext, TConfigurationDbContext, TPersistedGrantDbContext,
			TLogDbContext, TAuditLoggingDbContext>(this IServiceCollection services, IConfiguration configuration)
			where TIdentityDbContext : DbContext
			where TPersistedGrantDbContext : DbContext, IAdminPersistedGrantDbContext
			where TConfigurationDbContext : DbContext, IAdminConfigurationDbContext
			where TLogDbContext : DbContext, IAdminLogDbContext
			where TAuditLoggingDbContext : DbContext, IAuditLoggingDbContext<AuditLog>
		{
			var migrationsAssembly = typeof(Startup).GetTypeInfo().Assembly.GetName().Name;

			// Config DB for identity
			services.AddDbContext<TIdentityDbContext>(options =>
			{
				if (PostgresInUse)
				{
					options.UseNpgsql(
						configuration.GetConnectionString(ConfigurationConsts.IdentityDbConnectionStringKey),
						sql => sql.MigrationsAssembly(migrationsAssembly));
				}
				else
				{
					options.UseSqlServer(
					configuration.GetConnectionString(ConfigurationConsts.IdentityDbConnectionStringKey),
					sql => sql.MigrationsAssembly(migrationsAssembly));
				}
			});

			// Config DB from existing connection
			services.AddConfigurationDbContext<TConfigurationDbContext>(options =>
			{
				if (PostgresInUse)
				{
					options.ConfigureDbContext = b =>
						b.UseNpgsql(
							configuration.GetConnectionString(ConfigurationConsts.ConfigurationDbConnectionStringKey),
							sql => sql.MigrationsAssembly(migrationsAssembly));
				}
				else
				{
					options.ConfigureDbContext = b =>
					  b.UseSqlServer(
						  configuration.GetConnectionString(ConfigurationConsts.ConfigurationDbConnectionStringKey),
						  sql => sql.MigrationsAssembly(migrationsAssembly));
				}
			});

			// Operational DB from existing connection
			services.AddOperationalDbContext<TPersistedGrantDbContext>(options =>
			{
				if (PostgresInUse)
				{
					options.ConfigureDbContext = b =>
						b.UseNpgsql(
							configuration.GetConnectionString(ConfigurationConsts.PersistedGrantDbConnectionStringKey),
							sql => sql.MigrationsAssembly(migrationsAssembly));
				}
				else
				{
					options.ConfigureDbContext = b =>
						b.UseSqlServer(
							configuration.GetConnectionString(ConfigurationConsts.PersistedGrantDbConnectionStringKey),
							sql => sql.MigrationsAssembly(migrationsAssembly));
				}
			});

			// Log DB from existing connection
			services.AddDbContext<TLogDbContext>(options =>
			{
				if (PostgresInUse)
				{
					options.UseNpgsql(
						configuration.GetConnectionString(ConfigurationConsts.AdminLogDbConnectionStringKey),
						optionsSql => optionsSql.MigrationsAssembly(migrationsAssembly));
				} else
				{
					options.UseSqlServer(
						configuration.GetConnectionString(ConfigurationConsts.AdminLogDbConnectionStringKey),
						optionsSql => optionsSql.MigrationsAssembly(migrationsAssembly));
				}
			});

            // Audit logging connection
            services.AddDbContext<TAuditLoggingDbContext>(options =>
			{
				if (PostgresInUse)
				{
					options.UseNpgsql(
						configuration.GetConnectionString(ConfigurationConsts.AdminAuditLogDbConnectionStringKey),
						optionsSql => optionsSql.MigrationsAssembly(migrationsAssembly));

				}
				else
				{
					options.UseSqlServer(
						configuration.GetConnectionString(ConfigurationConsts.AdminAuditLogDbConnectionStringKey),
						optionsSql => optionsSql.MigrationsAssembly(migrationsAssembly));
				}
			});
        }

        /// <summary>
        /// Add authentication middleware for an API
        /// </summary>
        /// <typeparam name="TIdentityDbContext">DbContext for an access to Identity</typeparam>
        /// <typeparam name="TUser">Entity with User</typeparam>
        /// <typeparam name="TRole">Entity with Role</typeparam>
        /// <param name="services"></param>
        /// <param name="adminApiConfiguration"></param>
        public static void AddApiAuthentication<TIdentityDbContext, TUser, TRole>(this IServiceCollection services,
            AdminApiConfiguration adminApiConfiguration)
            where TIdentityDbContext : DbContext
            where TRole : class
            where TUser : class
        {
            services.AddAuthentication(IdentityServerAuthenticationDefaults.AuthenticationScheme)
                .AddIdentityServerAuthentication(options =>
                {
                    options.Authority = adminApiConfiguration.IdentityServerBaseUrl;
                    options.ApiName = adminApiConfiguration.OidcApiName;
                    options.RequireHttpsMetadata = adminApiConfiguration.RequireHttpsMetadata;
                });

            services.AddIdentity<TUser, TRole>(options => { options.User.RequireUniqueEmail = true; })
                .AddEntityFrameworkStores<TIdentityDbContext>()
                .AddDefaultTokenProviders();
        }

        public static void AddAuthorizationPolicies(this IServiceCollection services)
        {
            var adminApiConfiguration = services.BuildServiceProvider().GetService<AdminApiConfiguration>();

            services.AddAuthorization(options =>
            {
                options.AddPolicy(AuthorizationConsts.AdministrationPolicy,
                    policy =>
                        policy.RequireAssertion(context => context.User.HasClaim(c =>
                                (c.Type == JwtClaimTypes.Role && c.Value == adminApiConfiguration.AdministrationRole) ||
                                (c.Type == $"client_{JwtClaimTypes.Role}" && c.Value == adminApiConfiguration.AdministrationRole)
                            )
                        ));
            });
        }
    }
}