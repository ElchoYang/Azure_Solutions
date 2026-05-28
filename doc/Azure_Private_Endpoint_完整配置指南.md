# Azure Private Endpoint 完整配置指南

从零开始搭建安全的私有网络连接

---

## 目录

1. [什么是 Azure Private Endpoint](#1-什么是-azure-private-endpoint)
2. [前提条件与基础架构](#2-前提条件与基础架构)
3. [详细配置步骤（Azure 门户）](#3-详细配置步骤azure-门户)
4. [PowerShell 命令行配置](#4-powershell-命令行配置)
5. [DNS 私有区域配置](#5-dns-私有区域配置)
6. [支持的服务列表](#6-支持的服务列表)
7. [安全注意事项](#7-安全注意事项)
8. [定价信息](#8-定价信息)
9. [参考链接](#9-参考链接)

---

## 1. 什么是 Azure Private Endpoint

Azure Private Endpoint 是 Azure 私有链接（Private Link）的核心组件，它为您的虚拟网络提供通过私有 IP 地址安全连接到 Azure 托管服务的网络接口。通过私有终结点，您可以将 Azure PaaS 服务"引入"到您的虚拟网络中，完全消除公共互联网暴露。

### 1.1 核心特性

| 特性 | 说明 |
|------|------|
| 私有 IP 地址 | 从您的 VNet 地址空间分配，不会改变 |
| 流量路由 | 通过 Microsoft 主干网络传输，不经过公网 |
| 单向连接 | 仅支持客户端到服务的连接方向 |
| 区域要求 | 终结点必须与服务在同一区域（资源可跨区域） |
| DNS 解析 | 自动集成 Azure 私有 DNS 区域 |

### 1.2 连接状态

| 状态 | 说明 |
|------|------|
| Approved（已批准） | 连接就绪，可以使用 |
| Pending（待处理） | 等待资源所有者批准 |
| Rejected（已拒绝） | 被资源所有者拒绝 |
| Disconnected（已断开） | 连接已被所有者移除 |

---

## 2. 前提条件与基础架构

### 2.1 基础要求

- Azure 账户和有效订阅
- 虚拟网络（VNet）- 必须在创建终结点前存在
- 子网 - 建议使用专用子网
- 适当的资源权限（所有者、参与者或特定 Private Link 权限）

### 2.2 Virtual Network（虚拟网络）介绍

Azure 虚拟网络（VNet）是 Azure 云中的基础网络服务，类似于传统数据中心的企业网络。它提供了以下关键功能：

- **地址空间管理**：定义 VNet 的 IP 地址范围（如 10.0.0.0/16）
- **子网划分**：将 VNet 划分为多个子网，便于管理
- **网络隔离**：不同 VNet 默认隔离
- **DNS 配置**：自定义 DNS 服务器或使用 Azure 提供的 DNS
- **网络安全组（NSG）**：控制子网和 VM 的入站/出站流量
- **对等互连（VNet Peering）**：连接不同 VNet
- **VPN 网关/ExpressRoute**：连接本地网络

#### 创建 VNet 示例

| 参数 | 建议值 | 说明 |
|------|--------|------|
| 名称 | vnet-prod-001 | 有意义的命名规范 |
| 地址空间 | 10.0.0.0/16 | 预留足够空间 |
| 子网名称 | subnet-pe | 用于私有终结点 |
| 子网地址 | 10.0.1.0/24 | /27 或更大 |
| 区域 | East US 2 | 与资源一致 |

### 2.3 子网要求

- 子网必须在 Azure 虚拟网络内，且与私有终结点同一区域
- 建议使用专用子网用于私有终结点
- 网络策略（NSG、UDR）必须显式为私有终结点启用
- Azure Bastion 需要专用子网（名为 AzureBastionSubnet，至少 /26）
- 建议子网大小：/27 或更大，为未来扩展留空间

### 2.4 区域可用性

> ⚠️ **注意**：私有终结点不支持以下区域：West India、Australia Central 2、South Africa West、Brazil Southeast、所有政府区域、所有中国区域。

---

## 3. 详细配置步骤（Azure 门户）

### 3.1 第一步：创建资源组

1. 登录 Azure 门户 (https://portal.azure.com)
2. 搜索"资源组"并点击"创建"
3. 选择订阅和区域
4. 输入资源组名称（如：rg-private-endpoint-demo）
5. 点击"审阅 + 创建"完成创建

### 3.2 第二步：创建虚拟网络

1. 搜索"虚拟网络"并点击"创建"
2. 选择订阅和资源组
3. 输入虚拟网络名称（如：vnet-main）
4. 配置地址空间（如：10.0.0.0/16）
5. 添加子网：
   - 子网名称：subnet-private-endpoint
   - 子网地址范围：10.0.1.0/24
6. 点击"审阅 + 创建"完成创建

### 3.3 第三步：创建测试用 VM（可选但推荐）

1. 搜索"虚拟机"并点击"创建"
2. 配置基本设置：
   - 资源组：选择刚创建的资源组
   - 虚拟机名称：vm-test
   - 区域：与 VNet 一致
   - 镜像：Windows Server 2022 或 Ubuntu
3. 网络设置：
   - 虚拟网络：选择刚创建的 vnet-main
   - 子网：subnet-private-endpoint
   - 公共 IP：无（测试用可选）
4. 点击"审阅 + 创建"完成创建

### 3.4 第四步：创建目标 Azure 服务

以 Azure Blob Storage 为例，创建存储账户：

1. 搜索"存储账户"并点击"创建"
2. 选择订阅和资源组
3. 输入存储账户名称（全局唯一，如：stprivate001）
4. 选择区域和性能设置
5. 点击"审阅 + 创建"完成创建

### 3.5 第五步：创建私有终结点

1. 在存储账户左侧菜单选择"专用终结点连接"
2. 点击"+ 专用终结点"
3. **基础信息**：
   - 名称：pe-storage-blob
   - 区域：与存储账户一致
4. **资源**：
   - 连接方法：连接到库中的 Azure 资源
   - 订阅：您的订阅
   - 资源类型：Microsoft.Storage/storageAccounts
   - 资源：选择刚创建的存储账户
   - 目标子资源：blob
5. **虚拟网络**：
   - 虚拟网络：vnet-main
   - 子网：subnet-private-endpoint
   - 启用专用终结点：是
6. **DNS 配置**：
   - 选择"与资源集成"或"使用专用 DNS 区域"
7. 点击"审阅 + 创建"，然后点击"创建"

### 3.6 第六步：测试连接

1. 通过 Bastion 或 VPN 连接到 VNet 中的 VM
2. 在 VM 中打开命令行或 PowerShell
3. 测试私有 DNS 解析：
   ```powershell
   nslookup stprivate001.blob.core.windows.net
   ```
4. 应该返回私有 IP 地址（如 10.0.1.x）而不是公网 IP
5. 测试访问：尝试访问存储账户的 Blob 服务

---

## 4. PowerShell 命令行配置

### 4.1 安装 Azure PowerShell 模块

```powershell
# 安装 Azure PowerShell 模块
Install-Module -Name Az -AllowClobber -Scope CurrentUser
```

### 4.2 登录并设置上下文

```powershell
# 登录 Azure
Connect-AzAccount

# 设置订阅
$SubscriptionId = "your-subscription-id"
Select-AzSubscription -SubscriptionId $SubscriptionId
```

### 4.3 创建资源组和虚拟网络

```powershell
# 创建资源组
New-AzResourceGroup -Name 'rg-private-endpoint' -Location 'eastus2'

# 创建虚拟网络
$vnet = New-AzVirtualNetwork -Name 'vnet-main' `
    -ResourceGroupName 'rg-private-endpoint' `
    -Location 'eastus2' `
    -AddressPrefix '10.0.0.0/16'

# 添加子网配置
$subnet = Add-AzVirtualNetworkSubnetConfig -Name 'subnet-pe' `
    -VirtualNetwork $vnet `
    -AddressPrefix '10.0.1.0/24'

# 启用私有终结点网络策略
$subnet.PrivateEndpointNetworkPolicies = 'Enabled'

# 保存虚拟网络配置
Set-AzVirtualNetwork -VirtualNetwork $vnet
```

### 4.4 创建存储账户

```powershell
# 创建存储账户
New-AzStorageAccount -Name 'stprivate001' `
    -ResourceGroupName 'rg-private-endpoint' `
    -Location 'eastus2' `
    -SkuName 'Standard_LRS' `
    -Kind 'StorageV2'
```

### 4.5 创建私有终结点

```powershell
# 获取存储账户资源 ID
$storageAccount = Get-AzStorageAccount `
    -ResourceGroupName 'rg-private-endpoint' `
    -Name 'stprivate001'

# 创建私有链接服务连接
$pec = New-AzPrivateLinkServiceConnection `
    -Name 'pls-connection' `
    -PrivateLinkServiceId $storageAccount.Id `
    -GroupId 'blob'

# 获取虚拟网络
$vnet = Get-AzVirtualNetwork -Name 'vnet-main' `
    -ResourceGroupName 'rg-private-endpoint'

# 创建私有终结点
New-AzPrivateEndpoint `
    -Name 'pe-storage-blob' `
    -ResourceGroupName 'rg-private-endpoint' `
    -Location 'eastus2' `
    -Subnet $vnet.Subnets[0] `
    -PrivateLinkServiceConnection $pec
```

---

## 5. DNS 私有区域配置

DNS 配置是私有终结点工作的关键部分。正确的 DNS 设置确保域名解析到私有 IP 而不是公网 IP。

### 5.1 DNS 集成方案

| 方案 | 适用场景 |
|------|----------|
| VNet 工作负载（无解析器） | 仅 Azure VNet 内的工作负载 |
| 对等 VNet 工作负载 | 多个互联的 VNet |
| 本地工作负载 + DNS 转发器 | 需要从本地访问 Azure 服务 |
| 私有 DNS 解析器 | 复杂的混合 DNS 架构 |
| VNet + 本地 DNS 转发器 | 同时支持 VNet 和本地 |

### 5.2 主要服务的私有 DNS 区域

| Azure 服务 | 私有 DNS 区域名称 |
|------------|------------------|
| Azure Blob Storage | privatelink.blob.core.windows.net |
| Azure Files | privatelink.file.core.windows.net |
| Azure SQL Database | privatelink.database.windows.net |
| Azure Cosmos DB | privatelink.documents.azure.com |
| Azure Key Vault | privatelink.vaultcore.azure.net |
| Azure App Service | privatelink.azurewebsites.net |
| Azure Container Registry | privatelink.azurecr.io |
| Azure Event Hub | privatelink.servicebus.windows.net |

### 5.3 创建私有 DNS 区域（PowerShell）

```powershell
# 创建私有 DNS 区域
New-AzPrivateDnsZone `
    -ResourceGroupName 'rg-private-endpoint' `
    -Name 'privatelink.blob.core.windows.net'

# 创建虚拟网络链接
New-AzPrivateDnsVirtualNetworkLink `
    -ResourceGroupName 'rg-private-endpoint' `
    -ZoneName 'privatelink.blob.core.windows.net' `
    -Name 'dns-link' `
    -VirtualNetworkId $vnet.Id
```

### 5.4 DNS 最佳实践

- 使用服务推荐的 privatelink 前缀域名
- 为同一服务的每个私有终结点创建专用 DNS 区域
- 切勿覆盖用于公共终结点的活动 DNS 区域
- 对于本地工作负载：转发到公共 DNS 区域（而非 privatelink 区域）
- 考虑使用 DNS 区域组实现自动记录管理

---

## 6. 支持的服务列表

Azure Private Endpoint 支持 60+ 种 Azure 服务。

### 6.1 AI + 机器学习

| 服务 | 支持的子资源 |
|------|-------------|
| Azure Machine Learning | amlworkspace |
| Azure AI Search | search |
| Azure Bot Service | bot |
| Azure AI Video Indexer | videoindexer |

### 6.2 分析

| 服务 | 支持的子资源 |
|------|-------------|
| Azure Synapse Analytics | Sql |
| Azure Event Hubs | namespace |
| Azure Monitor | monitor |
| Azure Data Factory | datafactory |
| Azure Data Explorer | cluster |

### 6.3 数据库

| 服务 | 支持的子资源 |
|------|-------------|
| Azure SQL Database | sqlServer |
| Azure Cosmos DB | sql, mongodb, cassandra, gremlin, table |
| Azure Database for PostgreSQL | postgresqlServer |
| Azure Database for MySQL | mysqlServer |
| Azure Database for MariaDB | mariadbServer |
| Azure Cache for Redis | redis |

### 6.4 存储

| 服务 | 支持的子资源 |
|------|-------------|
| Azure Blob Storage | blob |
| Azure Files | file |
| Azure Queue Storage | queue |
| Azure Table Storage | table |
| Azure Data Lake Gen2 | dfs |

### 6.5 Web

| 服务 | 支持的子资源 |
|------|-------------|
| Azure App Service | sites |
| Azure App Service (Logic Apps) | workflow |
| Azure SignalR Service | signalr |
| Azure Static Web Apps | staticwebapps |

---

## 7. 安全注意事项

### 7.1 网络安全

| 安全方面 | 说明 |
|----------|------|
| 公共访问 | 不会自动限制；需要额外配置 |
| NSG 支持 | 支持（必须显式启用） |
| ASG 支持 | 支持（每个 ASG 最多 50 个 IP 配置） |
| UDR 支持 | 支持 |
| NSG 流日志 | 入站流量不可用 |

### 7.2 重要安全说明

- 私有终结点不会自动阻止公共网络访问
- 某些服务（如 Azure Cosmos DB）可能需要打开所有目标端口
- 使用网络安全边界（Network Security Perimeters）进行额外 PaaS 保护
- 定期审计私有终结点配置和连接状态
- 监控 DNS 解析，确保没有意外泄漏到公网

### 7.3 限制静态 IP 的服务

| 服务 | 说明 |
|------|------|
| Azure Kubernetes Service (AKS) | 不支持静态 IP |
| Azure Application Gateway | 不支持静态 IP |
| HDInsight | 不支持静态 IP |
| Recovery Services Vaults | 不支持静态 IP |

---

## 8. 定价信息

Azure Private Link （包括私有终结点）采用按需付费模式。

| 组件 | 价格 |
|------|------|
| 私有终结点 | 按小时计费（部分小时按整小时计费） |
| 入站数据处理 | $0.005/GB（0-1 PB 层级） |
| 出站数据处理 | 遵循标准 Azure 数据传输定价 |
| 私有链接服务 | 免费（不收取费用） |

> 注：实际价格可能因区域而异。具体定价请参考 Azure 门户或 Azure 定价计算器。

---

## 9. 参考链接

### 9.1 核心文档

- [Azure Private Endpoint 概述](https://learn.microsoft.com/zh-cn/azure/private-link/private-endpoint-overview)
- [Azure Private Link 概述](https://learn.microsoft.com/zh-cn/azure/private-link/private-link-overview)
- [Azure Private Link 可用性](https://learn.microsoft.com/zh-cn/azure/private-link/availability)

### 9.2 创建指南

- [通过 Azure 门户创建私有终结点](https://learn.microsoft.com/zh-cn/azure/private-link/create-private-endpoint-portal)
- [通过 PowerShell 创建私有终结点](https://learn.microsoft.com/zh-cn/azure/private-link/create-private-endpoint-powershell)
- [通过 ARM 模板创建私有终结点](https://learn.microsoft.com/zh-cn/azure/private-link/create-private-endpoint-template)
- [通过 Azure CLI 创建私有终结点](https://learn.microsoft.com/zh-cn/azure/private-link/create-private-endpoint-cli)

### 9.3 DNS 配置

- [私有终结点 DNS 集成](https://learn.microsoft.com/zh-cn/azure/private-link/private-endpoint-dns-integration)
- [虚拟网络 DNS 设置](https://learn.microsoft.com/zh-cn/azure/virtual-network/virtual-networks-name-resolution-for-vms-and-role-instances)

### 9.4 安全相关

- [网络安全边界概念](https://learn.microsoft.com/zh-cn/azure/private-link/network-security-perimeter-concepts)
- [禁用私有终结点网络策略](https://learn.microsoft.com/zh-cn/azure/private-link/disable-private-endpoint-network-policy)

### 9.5 定价与培训

- [Azure Private Link 定价](https://azure.microsoft.com/zh-cn/pricing/details/private-link/)
- [Microsoft Learn 培训路径](https://learn.microsoft.com/zh-cn/training/paths/implement-private-link/)

---

*文档生成时间：2026年5月*
