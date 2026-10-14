using DBViewer.Models;
namespace DBViewer.Services;
public static class DemoSchema
{
    public static DatabaseSchema Create()
    {
        ColumnSchema C(string n,string t,string? l,string comment,bool pk=false,bool nullable=false,string? value=null) => new(n,t,l,nullable,pk,value,comment);
        TableSchema T(string n,string comment,params ColumnSchema[] c) => new("public",n,comment,c.ToList());
        var tables = new List<TableSchema> {
            T("users","用户信息 · 存储账户与个人资料", C("id","bigint",null,"用户唯一标识",true,false,"identity"), C("username","varchar","50","用户登录名称"), C("email","varchar","255","电子邮箱地址"), C("password_hash","varchar","255","密码哈希值"), C("avatar_url","varchar","500","用户头像地址",false,true), C("status","smallint",null,"账户状态：1 正常，0 禁用",false,false,"1"), C("created_at","timestamp",null,"创建时间",false,false,"CURRENT_TIMESTAMP"), C("updated_at","timestamp",null,"最后更新时间",false,true)),
            T("orders","订单主表",C("id","bigint",null,"订单 ID",true),C("user_id","bigint",null,"下单用户"),C("order_number","varchar","64","订单编号"),C("total_amount","numeric","12,2","订单总额"),C("status","varchar","20","订单状态",false,false,"'pending'"),C("created_at","timestamp",null,"创建时间")),
            T("order_items","订单明细",C("id","bigint",null,"明细 ID",true),C("order_id","bigint",null,"关联订单"),C("product_id","bigint",null,"关联商品"),C("quantity","integer",null,"购买数量"),C("unit_price","numeric","12,2","成交单价")),
            T("products","商品信息",C("id","bigint",null,"商品 ID",true),C("category_id","bigint",null,"所属分类"),C("name","varchar","200","商品名称"),C("price","numeric","12,2","商品价格"),C("stock","integer",null,"库存数量",false,false,"0"),C("description","text",null,"商品描述",false,true)),
            T("categories","商品分类",C("id","bigint",null,"分类 ID",true),C("name","varchar","100","分类名称"),C("parent_id","bigint",null,"父级分类",false,true)),
            T("addresses","收货地址",C("id","bigint",null,"地址 ID",true),C("user_id","bigint",null,"所属用户"),C("recipient","varchar","100","收件人"),C("city","varchar","100","城市"),C("detail","varchar","500","详细地址")),
            T("payments","支付记录",C("id","bigint",null,"支付 ID",true),C("order_id","bigint",null,"关联订单"),C("amount","numeric","12,2","支付金额"),C("method","varchar","30","支付方式"),C("paid_at","timestamp",null,"支付时间",false,true)),
            T("reviews","商品评价",C("id","bigint",null,"评价 ID",true),C("user_id","bigint",null,"评价用户"),C("product_id","bigint",null,"评价商品"),C("rating","smallint",null,"评分 1–5"),C("content","text",null,"评价内容",false,true))
        };
        Relation R(string f,string c,string t,string tc="id") => new("public",f,c,"public",t,tc);
        return new("ecommerce_demo","postgresql",tables,new() { R("orders","user_id","users"),R("order_items","order_id","orders"),R("order_items","product_id","products"),R("products","category_id","categories"),R("categories","parent_id","categories"),R("addresses","user_id","users"),R("payments","order_id","orders"),R("reviews","user_id","users"),R("reviews","product_id","products") });
    }
}
